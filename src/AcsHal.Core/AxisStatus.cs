namespace AcsHal.Core;

/// <summary>Snapshot of one controller axis. Positions and velocities are in controller user units.</summary>
public sealed record AxisStatus(
    int Index,
    double FeedbackPosition,
    double ReferencePosition,
    double FeedbackVelocity,
    bool Enabled,
    bool Moving,
    bool InPosition,
    AxisFaults Faults)
{
    public bool HasFault => Faults != AxisFaults.None;
}

/// <summary>
/// Axis fault bits. Values match the ACSPL+ FAULT variable bits so an implementation can cast directly;
/// bits not named here are still preserved in the value.
/// </summary>
[Flags]
public enum AxisFaults : uint
{
    None = 0,
    RightLimit = 1u << 0,
    LeftLimit = 1u << 1,
    NetworkError = 1u << 2,
    Overheat = 1u << 4,
    SoftwareRightLimit = 1u << 5,
    SoftwareLeftLimit = 1u << 6,
    EncoderNotConnected = 1u << 7,
    Encoder2NotConnected = 1u << 8,
    DriveFault = 1u << 9,
    EncoderError = 1u << 10,
    Encoder2Error = 1u << 11,
    PositionError = 1u << 12,
    CriticalPositionError = 1u << 13,
    VelocityLimit = 1u << 14,
    AccelerationLimit = 1u << 15,
    CurrentLimit = 1u << 16,
    ServoProcessorAlarm = 1u << 17,
    SafeTorqueOff = 1u << 18,
    EmergencyStop = 1u << 28,
}
