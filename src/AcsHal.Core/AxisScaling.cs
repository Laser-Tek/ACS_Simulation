namespace AcsHal.Core;

public sealed record ScalingResult(AxisConfig Axis, double Expected, double Actual)
{
    // The controller stores the scale as a double; allow for decimal round-tripping only.
    public bool Matches => Math.Abs(Expected - Actual) <= 1e-9 * Math.Abs(Expected);
}

/// <summary>
/// Compares the controller's units-per-count scaling with the axis mechanics in the machine config.
/// On real hardware the scale is set during commissioning and saved on the controller, so it should
/// only be verified; <see cref="Apply"/> is for the simulator.
/// </summary>
public static class AxisScaling
{
    public static IReadOnlyList<ScalingResult> Verify(IMotionController controller, MachineConfig config) =>
        config.Axes
            .Where(a => a.Mechanics is not null)
            .Select(a => new ScalingResult(a, a.Mechanics!.UnitsPerCount, controller.GetAxis(a.Index).GetUnitsPerCount()))
            .ToList();

    /// <summary>Writes the configured scale to every axis that has mechanics. The axes must be disabled.</summary>
    public static void Apply(IMotionController controller, MachineConfig config)
    {
        foreach (var axis in config.Axes.Where(a => a.Mechanics is not null))
            controller.GetAxis(axis.Index).SetUnitsPerCount(axis.Mechanics!.UnitsPerCount);
    }
}
