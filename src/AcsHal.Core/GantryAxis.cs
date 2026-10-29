namespace AcsHal.Core;

/// <summary>Position of a dual-drive gantry: where it is along its travel, and how far the two sides disagree.</summary>
public sealed record GantryPosition(double Position, double Yaw);

/// <summary>
/// One logical axis made of two motors in true-gantry mode. Once engaged, the COG axis moves both
/// motors and its position is their average; the yaw axis position is their difference.
/// </summary>
public sealed class GantryAxis
{
    private readonly IMotionController _controller;

    public GantryAxis(IMotionController controller, MachineConfig config, GantryConfig gantry)
    {
        _controller = controller;
        Name = gantry.Name;
        CogConfig = config.Axis(gantry.CogAxis);
        YawConfig = config.Axis(gantry.YawAxis);
        Cog = controller.GetAxis(CogConfig.Index);
        Yaw = controller.GetAxis(YawConfig.Index);
    }

    public string Name { get; }
    public AxisConfig CogConfig { get; }
    public AxisConfig YawConfig { get; }

    /// <summary>Moves the gantry once engaged. Enabling or disabling it enables or disables both motors.</summary>
    public IAxis Cog { get; }

    public IAxis Yaw { get; }

    public bool IsEngaged => _controller.GetGantryMode(Cog.Index) && _controller.GetGantryMode(Yaw.Index);

    /// <summary>Disables both motors and switches the pair into true-gantry mode.</summary>
    public Task EngageAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SetModeAsync(true, timeout, cancellationToken);

    /// <summary>Disables both motors and returns them to independent axes.</summary>
    public Task ReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        SetModeAsync(false, timeout, cancellationToken);

    /// <summary>
    /// Gantry position from the two axes' statuses. Engaged, the controller already reports average and
    /// difference; released, they are computed from the two independent positions.
    /// </summary>
    public static GantryPosition PositionFrom(bool engaged, AxisStatus cog, AxisStatus yaw) =>
        engaged
            ? new GantryPosition(cog.FeedbackPosition, yaw.FeedbackPosition)
            : new GantryPosition((cog.FeedbackPosition + yaw.FeedbackPosition) / 2, yaw.FeedbackPosition - cog.FeedbackPosition);

    private async Task SetModeAsync(bool engaged, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Cog.Disable();
        Yaw.Disable();
        await Cog.WaitEnabledAsync(false, timeout, cancellationToken).ConfigureAwait(false);
        await Yaw.WaitEnabledAsync(false, timeout, cancellationToken).ConfigureAwait(false);
        _controller.SetGantryMode(Cog.Index, engaged);
        _controller.SetGantryMode(Yaw.Index, engaged);
    }
}
