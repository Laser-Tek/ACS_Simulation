using AcsHal.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcsHal.Viewer.ViewModels;

/// <summary>Settings shared by every axis card, owned by the main view model.</summary>
public interface IAxisHost
{
    bool CanCommand { get; }
    double JogSpeed { get; }
    double MoveSpeed { get; }
    void Log(string message);
}

/// <summary>One axis of the machine as an operator sees it: a gantry (X, Y) or a single axis (Z, Theta, doors).</summary>
public abstract partial class MachineAxisViewModel : ObservableObject
{
    protected static readonly TimeSpan ModeChangeTimeout = TimeSpan.FromSeconds(5);

    protected MachineAxisViewModel(IAxisHost host, string name, TravelRange? travel)
    {
        Host = host;
        Name = name;
        TravelMin = travel?.Min ?? 0;
        TravelMax = travel?.Max ?? 100;
        HasTravel = travel is not null;
    }

    protected IAxisHost Host { get; }

    public string Name { get; }
    public double TravelMin { get; }
    public double TravelMax { get; }
    public bool HasTravel { get; }
    public virtual bool IsGantry => false;

    /// <summary>Axis that receives motion commands (the COG axis for a gantry).</summary>
    protected abstract IAxis MotionAxis { get; }

    [ObservableProperty] private double _position;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _moving;
    [ObservableProperty] private bool _inPosition;
    [ObservableProperty] private bool _hasFault;
    [ObservableProperty] private string _faults = "";
    [ObservableProperty] private double _target;
    [ObservableProperty] private bool _canMove;

    /// <summary>Position as a fraction of travel, 0..1, for drawing.</summary>
    public double Fraction => Math.Clamp((Position - TravelMin) / (TravelMax - TravelMin), 0, 1);

    partial void OnPositionChanged(double value) => OnPropertyChanged(nameof(Fraction));

    public abstract void Update(IReadOnlyDictionary<int, AxisStatus> statuses);

    /// <summary>Re-evaluates whether motion commands are allowed (connection, simulator, gantry mode).</summary>
    public virtual void RefreshPermissions() => CanMove = Host.CanCommand;

    [RelayCommand]
    private void Enable() => Run("enable", MotionAxis.Enable);

    [RelayCommand]
    private void Disable() => Run("disable", MotionAxis.Disable);

    [RelayCommand]
    private void ClearFaults() => Run("clear faults", MotionAxis.ClearFaults);

    [RelayCommand]
    private void Halt() => Run("halt", MotionAxis.Halt);

    [RelayCommand]
    private void Move() => MoveTo(Target);

    public void MoveTo(double target)
    {
        if (HasTravel && (target < TravelMin || target > TravelMax))
        {
            Host.Log($"{Name}: target {target:0.###} is outside travel {TravelMin:0.###}..{TravelMax:0.###}; not moving");
            return;
        }
        Run($"move to {target:0.###}", () =>
        {
            var speed = Host.MoveSpeed;
            MotionAxis.SetMotionProfile(new MotionProfile(Velocity: speed, Acceleration: speed * 10, Deceleration: speed * 10, Jerk: speed * 100));
            MotionAxis.MoveAbsolute(target);
        });
    }

    public void StartJog(int direction)
    {
        if (CanMove)
            Run("jog", () => MotionAxis.Jog(direction * Host.JogSpeed));
    }

    public void StopJog()
    {
        if (CanMove)
            Run("stop jog", MotionAxis.Halt);
    }

    protected void Run(string what, Action action)
    {
        if (!Host.CanCommand)
            return;
        try
        {
            action();
        }
        catch (Exception ex) when (ex is MotionControllerException or TimeoutException or InvalidOperationException)
        {
            Host.Log($"{Name}: {what} failed: {ex.Message}");
        }
    }

    protected void ApplyStatus(double position, AxisStatus a, AxisStatus? b = null)
    {
        Position = position;
        Enabled = a.Enabled && (b?.Enabled ?? true);
        Moving = a.Moving || (b?.Moving ?? false);
        InPosition = a.InPosition && (b?.InPosition ?? true);
        var faults = a.Faults | (b?.Faults ?? AxisFaults.None);
        HasFault = faults != AxisFaults.None;
        Faults = HasFault ? faults.ToString() : "";
    }
}

public sealed class SingleAxisViewModel(IAxisHost host, AxisConfig config, IAxis axis)
    : MachineAxisViewModel(host, config.Name, config.Travel)
{
    public int Index => axis.Index;
    protected override IAxis MotionAxis => axis;

    public override void Update(IReadOnlyDictionary<int, AxisStatus> statuses)
    {
        if (statuses.TryGetValue(axis.Index, out var s))
            ApplyStatus(s.FeedbackPosition, s);
    }
}

public sealed partial class GantryViewModel : MachineAxisViewModel
{
    private readonly GantryAxis _gantry;

    public GantryViewModel(IAxisHost host, GantryAxis gantry)
        : base(host, gantry.Name, gantry.CogConfig.Travel)
    {
        _gantry = gantry;
    }

    public override bool IsGantry => true;
    public string Motors => $"{_gantry.CogConfig.Name} (axis {_gantry.Cog.Index}) + {_gantry.YawConfig.Name} (axis {_gantry.Yaw.Index})";
    protected override IAxis MotionAxis => _gantry.Cog;

    [ObservableProperty] private double _yaw;
    [ObservableProperty] private bool _isEngaged;

    partial void OnIsEngagedChanged(bool value) => RefreshPermissions();

    // Moving one side of a gantry on its own would skew it, so motion is only offered once engaged.
    public override void RefreshPermissions() => CanMove = Host.CanCommand && IsEngaged;

    /// <summary>Reads the gantry mode from the controller. Called from the polling thread.</summary>
    public bool ReadEngaged() => _gantry.IsEngaged;

    public override void Update(IReadOnlyDictionary<int, AxisStatus> statuses)
    {
        if (statuses.TryGetValue(_gantry.Cog.Index, out var cog) && statuses.TryGetValue(_gantry.Yaw.Index, out var yaw))
        {
            var p = GantryAxis.PositionFrom(IsEngaged, cog, yaw);
            ApplyStatus(p.Position, cog, yaw);
            Yaw = p.Yaw;
        }
    }

    [RelayCommand]
    private async Task EngageAsync() => await SetModeAsync(true);

    [RelayCommand]
    private async Task ReleaseAsync() => await SetModeAsync(false);

    private async Task SetModeAsync(bool engage)
    {
        if (!Host.CanCommand)
            return;
        try
        {
            if (engage) await _gantry.EngageAsync(ModeChangeTimeout);
            else await _gantry.ReleaseAsync(ModeChangeTimeout);
            IsEngaged = _gantry.IsEngaged;
            Host.Log($"{Name}: gantry mode {(IsEngaged ? "engaged" : "released")}");
        }
        catch (Exception ex) when (ex is MotionControllerException or TimeoutException)
        {
            Host.Log($"{Name}: {(engage ? "engage" : "release")} failed: {ex.Message}");
        }
    }
}

/// <summary>One raw controller axis, for the physical-axes table.</summary>
public sealed partial class PhysicalAxisViewModel(string name, int index) : ObservableObject
{
    public string Name { get; } = name;
    public int Index { get; } = index;

    [ObservableProperty] private double _position;
    [ObservableProperty] private double _velocity;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _moving;
    [ObservableProperty] private string _faults = "";

    public void Update(AxisStatus s)
    {
        Position = s.FeedbackPosition;
        Velocity = s.FeedbackVelocity;
        Enabled = s.Enabled;
        Moving = s.Moving;
        Faults = s.HasFault ? s.Faults.ToString() : "";
    }
}
