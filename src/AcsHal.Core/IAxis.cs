namespace AcsHal.Core;

/// <summary>
/// One controller axis. Commands return as soon as the controller accepts them; use the Wait methods
/// to wait for the result.
/// </summary>
public interface IAxis
{
    int Index { get; }

    AxisStatus GetStatus();

    void Enable();
    void Disable();
    void ClearFaults();

    void SetMotionProfile(MotionProfile profile);

    /// <summary>Position units per encoder count (EFAC on ACS).</summary>
    double GetUnitsPerCount();

    /// <summary>Sets position units per encoder count. The axis must be disabled.</summary>
    void SetUnitsPerCount(double unitsPerCount);

    void MoveAbsolute(double position);
    void MoveRelative(double distance);

    /// <summary>Constant-velocity motion; the sign sets the direction. Stop with <see cref="Halt"/>.</summary>
    void Jog(double velocity);

    /// <summary>Stops with the normal deceleration.</summary>
    void Halt();

    /// <summary>Stops with the kill deceleration.</summary>
    void Kill();

    Task WaitEnabledAsync(bool enabled, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Waits until motion ends. Throws <see cref="AxisFaultException"/> if the axis is disabled while waiting.</summary>
    Task WaitMotionDoneAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
