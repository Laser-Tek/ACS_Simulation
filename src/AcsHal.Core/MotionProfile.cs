namespace AcsHal.Core;

/// <summary>Motion parameters in controller user units. A null value leaves the controller's current setting unchanged.</summary>
public sealed record MotionProfile(
    double? Velocity = null,
    double? Acceleration = null,
    double? Deceleration = null,
    double? Jerk = null,
    double? KillDeceleration = null);
