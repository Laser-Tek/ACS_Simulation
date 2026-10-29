using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AcsHal.Acs;
using AcsHal.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcsHal.Viewer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAxisHost
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private const int GantryModeRefreshEvery = 20; // polls, i.e. about once a second
    private const int MaxLogLines = 500;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private AcsMotionController? _controller;
    private MachineConfig? _config;
    private CancellationTokenSource? _pollCancel;
    private Task? _pollTask;

    public MainViewModel()
    {
        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        if (Directory.Exists(configDir))
            foreach (var file in Directory.GetFiles(configDir, "*.json").Order())
                ConfigFiles.Add(file);
        SelectedConfig = ConfigFiles.FirstOrDefault(f => f.EndsWith("machine.sim.json", StringComparison.OrdinalIgnoreCase))
                         ?? ConfigFiles.FirstOrDefault();
    }

    public ObservableCollection<string> ConfigFiles { get; } = [];
    public ObservableCollection<MachineAxisViewModel> MachineAxes { get; } = [];
    public ObservableCollection<SingleAxisViewModel> StrokeAxes { get; } = [];
    public ObservableCollection<PhysicalAxisViewModel> PhysicalAxes { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty] private string? _selectedConfig;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSimulator;
    [ObservableProperty] private string _controllerInfo = "Not connected";
    [ObservableProperty] private double _jogSpeed = 20;
    [ObservableProperty] private double _moveSpeed = 50;
    [ObservableProperty] private string _terminalInput = "";
    [ObservableProperty] private GantryViewModel? _xAxis;
    [ObservableProperty] private GantryViewModel? _yAxis;

    /// <summary>Motion and raw commands are only offered against the simulator; hardware is read-only.</summary>
    public bool CanCommand => IsConnected && IsSimulator;

    public string ModeText => !IsConnected ? "" : IsSimulator ? "SIMULATOR" : "HARDWARE (read-only)";

    partial void OnIsConnectedChanged(bool value) => RefreshPermissions();
    partial void OnIsSimulatorChanged(bool value) => RefreshPermissions();

    public void Log(string message)
    {
        void Append()
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss.fff}  {message}");
            while (LogLines.Count > MaxLogLines)
                LogLines.RemoveAt(0);
        }
        if (_dispatcher.CheckAccess()) Append();
        else _dispatcher.InvokeAsync(Append);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected || SelectedConfig is null)
            return;
        IsBusy = true;
        try
        {
            var config = MachineConfig.Load(SelectedConfig);
            var controller = new AcsMotionController();
            var simulator = config.Connection.Mode == ConnectionMode.Simulator;
            Log($"Connecting with {Path.GetFileName(SelectedConfig)} ...");

            // Connecting and setup block on the controller, so keep them off the UI thread.
            var info = await Task.Run(async () =>
            {
                controller.Connect(config.Connection);
                var info = controller.GetInfo();
                if (info.AxisCount <= config.Axes.Max(a => a.Index))
                    throw new InvalidDataException($"Controller has {info.AxisCount} axes; the config needs index {config.Axes.Max(a => a.Index)}.");
                await PrepareAsync(controller, config, simulator);
                return info;
            });

            _controller = controller;
            _config = config;
            BuildAxes(controller, config);
            IsSimulator = simulator;
            IsConnected = true;
            ControllerInfo = $"Firmware {info.FirmwareVersion} · library {info.LibraryVersion} · {info.AxisCount} axes";
            Log($"Connected: {ControllerInfo}");
            StartPolling();
        }
        catch (Exception ex) when (ex is MotionControllerException or TimeoutException or IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            Log($"Connect failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Simulator: start from a known state (all disabled, EFAC from config, gantries engaged).
    /// Hardware: only check that the controller's scaling matches the config.
    /// </summary>
    private async Task PrepareAsync(AcsMotionController controller, MachineConfig config, bool simulator)
    {
        if (simulator)
        {
            controller.DisableAll();
            foreach (var axis in config.Axes)
                await controller.GetAxis(axis.Index).WaitEnabledAsync(false, TimeSpan.FromSeconds(5));
            AxisScaling.Apply(controller, config);
            foreach (var gantry in config.Gantries)
                await new GantryAxis(controller, config, gantry).EngageAsync(TimeSpan.FromSeconds(5));
            Log("Simulator prepared: axes disabled, EFAC applied, gantries engaged");
        }
        else
        {
            foreach (var result in AxisScaling.Verify(controller, config).Where(r => !r.Matches))
                Log($"WARNING {result.Axis.Name}: controller EFAC {result.Actual:0.#########} != config {result.Expected:0.#########}");
        }
    }

    private void BuildAxes(AcsMotionController controller, MachineConfig config)
    {
        MachineAxes.Clear();
        StrokeAxes.Clear();
        PhysicalAxes.Clear();

        var gantryMembers = config.Gantries.SelectMany(g => new[] { g.CogAxis, g.YawAxis }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gantry in config.Gantries)
        {
            var vm = new GantryViewModel(this, new GantryAxis(controller, config, gantry));
            vm.IsEngaged = vm.ReadEngaged();
            MachineAxes.Add(vm);
        }
        foreach (var axis in config.Axes.Where(a => !gantryMembers.Contains(a.Name)))
        {
            var vm = new SingleAxisViewModel(this, axis, controller.GetAxis(axis.Index));
            MachineAxes.Add(vm);
            StrokeAxes.Add(vm);
        }
        foreach (var axis in config.Axes.OrderBy(a => a.Index))
            PhysicalAxes.Add(new PhysicalAxisViewModel(axis.Name, axis.Index));

        XAxis = MachineAxes.OfType<GantryViewModel>().FirstOrDefault(g => g.Name.Equals("X", StringComparison.OrdinalIgnoreCase));
        YAxis = MachineAxes.OfType<GantryViewModel>().FirstOrDefault(g => g.Name.Equals("Y", StringComparison.OrdinalIgnoreCase));
        RefreshPermissions();
    }

    private void StartPolling()
    {
        var controller = _controller!;
        var indices = _config!.Axes.Select(a => a.Index).ToList();
        var gantries = MachineAxes.OfType<GantryViewModel>().ToList();
        _pollCancel = new CancellationTokenSource();
        var token = _pollCancel.Token;

        _pollTask = Task.Run(async () =>
        {
            var reportedError = false;
            for (var tick = 0; !token.IsCancellationRequested; tick++)
            {
                try
                {
                    var statuses = controller.GetStatuses(indices).ToDictionary(s => s.Index);
                    // Gantry mode can also change from the terminal, so re-read it now and then.
                    var engaged = tick % GantryModeRefreshEvery == 0 ? gantries.Select(g => g.ReadEngaged()).ToList() : null;
                    await _dispatcher.InvokeAsync(() => Apply(statuses, gantries, engaged));
                    reportedError = false;
                }
                catch (Exception ex) when (ex is MotionControllerException or InvalidOperationException)
                {
                    if (!reportedError)
                        Log($"Status polling failed: {ex.Message}");
                    reportedError = true;
                }
                try { await Task.Delay(PollInterval, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void Apply(Dictionary<int, AxisStatus> statuses, List<GantryViewModel> gantries, List<bool>? engaged)
    {
        if (engaged is not null)
            for (var i = 0; i < gantries.Count; i++)
                gantries[i].IsEngaged = engaged[i];
        foreach (var axis in MachineAxes)
            axis.Update(statuses);
        foreach (var row in PhysicalAxes)
            if (statuses.TryGetValue(row.Index, out var s))
                row.Update(s);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected)
            return;
        await StopPollingAsync();
        try { _controller?.Disconnect(); }
        catch (MotionControllerException ex) { Log($"Disconnect: {ex.Message}"); }
        _controller = null;
        _config = null;
        IsConnected = false;
        IsSimulator = false;
        XAxis = YAxis = null;
        MachineAxes.Clear();
        StrokeAxes.Clear();
        PhysicalAxes.Clear();
        ControllerInfo = "Not connected";
        Log("Disconnected");
    }

    private async Task StopPollingAsync()
    {
        _pollCancel?.Cancel();
        if (_pollTask is not null)
        {
            // The loop marshals to the UI thread; wait without blocking it.
            try { await _pollTask; }
            catch (OperationCanceledException) { }
        }
        _pollCancel = null;
        _pollTask = null;
    }

    [RelayCommand]
    private void KillAll() => RunController("Kill all", c => c.KillAll());

    [RelayCommand]
    private void DisableAll() => RunController("Disable all", c => c.DisableAll());

    [RelayCommand]
    private void SendTerminal()
    {
        var command = TerminalInput.Trim();
        if (command.Length == 0 || _controller is null)
            return;
        // On hardware only queries ("?...") are allowed, so the terminal cannot start motion there.
        if (!IsSimulator && !command.StartsWith('?'))
        {
            Log($"Refused on hardware (read-only): {command}");
            return;
        }
        try
        {
            var reply = _controller.Transaction(command).TrimEnd();
            Log($"> {command}");
            foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log($"  {line.TrimEnd('\r')}");
            TerminalInput = "";
        }
        catch (MotionControllerException ex)
        {
            Log($"> {command}  !! {ex.Message}");
        }
    }

    /// <summary>Moves X and Y to fractions (0..1) of their travel, e.g. from a click on the table view.</summary>
    public void MoveXyTo(double xFraction, double yFraction)
    {
        if (XAxis is not { CanMove: true } x || YAxis is not { CanMove: true } y)
        {
            Log("Click-to-move needs both gantries engaged and enabled on the simulator");
            return;
        }
        x.MoveTo(Math.Round(x.TravelMin + xFraction * (x.TravelMax - x.TravelMin), 3));
        y.MoveTo(Math.Round(y.TravelMin + yFraction * (y.TravelMax - y.TravelMin), 3));
    }

    public async Task ShutdownAsync() => await DisconnectAsync();

    private void RunController(string what, Action<IMotionController> action)
    {
        if (_controller is null)
            return;
        // Stopping is always allowed, also on hardware.
        try
        {
            action(_controller);
            Log(what);
        }
        catch (MotionControllerException ex)
        {
            Log($"{what} failed: {ex.Message}");
        }
    }

    private void RefreshPermissions()
    {
        OnPropertyChanged(nameof(CanCommand));
        OnPropertyChanged(nameof(ModeText));
        foreach (var axis in MachineAxes)
            axis.RefreshPermissions();
    }
}
