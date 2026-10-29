namespace AcsHal.Core;

public sealed record ControllerInfo(string FirmwareVersion, string LibraryVersion, int AxisCount);

/// <summary>
/// Connection to one motion controller (real or simulated). Only <see cref="Connect"/> differs between
/// the simulator and hardware; everything else behaves the same.
/// </summary>
public interface IMotionController : IDisposable
{
    bool IsConnected { get; }

    void Connect(ConnectionConfig connection);
    void Disconnect();

    ControllerInfo GetInfo();

    /// <summary>Controller axis by index. In gantry mode, the COG and yaw axes are addressed by their indices too.</summary>
    IAxis GetAxis(int index);

    /// <summary>Reads one element of a controller variable (index -1 for a scalar), e.g. ReadScalar("MFLAGS", 0).</summary>
    double ReadScalar(string variable, int index = -1);

    /// <summary>Sends a raw controller command (ACSPL+ for ACS) and returns the reply, as the MMI terminal would.</summary>
    string Transaction(string command);

    void DisableAll();
    void KillAll();
}
