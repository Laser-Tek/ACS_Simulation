using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AcsHal.Viewer.ViewModels;

namespace AcsHal.Viewer;

public partial class MainWindow : Window
{
    private MachineAxisViewModel? _jogging;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        // Keep the newest log line in view.
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    // Jog runs while the button is held and halts on release or when the pointer leaves the button.
    private void OnJogPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MachineAxisViewModel axis, Tag: string tag })
        {
            _jogging = axis;
            axis.StartJog(int.Parse(tag));
        }
    }

    private void OnJogRelease(object sender, MouseEventArgs e)
    {
        _jogging?.StopJog();
        _jogging = null;
    }

    private void OnXyCanvasClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(XyCanvas);
        ViewModel.MoveXyTo(p.X / XyCanvas.Width, 1 - p.Y / XyCanvas.Height);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing || !ViewModel.IsConnected)
            return;
        // Stop polling and close the channel before the window goes away.
        e.Cancel = true;
        _closing = true;
        await ViewModel.ShutdownAsync();
        Close();
    }
}
