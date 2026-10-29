using ACS.SPiiPlusNET;
using AcsHal.Core;

namespace AcsHal.Acs;

/// <summary><see cref="IMotionController"/> backed by the ACS SPiiPlus .NET library.</summary>
public sealed class AcsMotionController : IMotionController
{
    private readonly Api _api = new();

    public bool IsConnected { get; private set; }

    public void Connect(ConnectionConfig connection)
    {
        if (IsConnected)
            throw new InvalidOperationException("Already connected.");

        AcsCall.Run(() =>
        {
            switch (connection.Mode)
            {
                case ConnectionMode.Simulator:
                    // Starts the SPiiPlus simulator through the User Mode Driver if it is not running yet.
                    _api.OpenCommSimulator();
                    break;
                case ConnectionMode.Ethernet:
                    _api.OpenCommEthernetTCP(connection.Address, connection.Port);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(connection), connection.Mode, "Unknown connection mode.");
            }
        }, $"Connect ({connection.Mode})");

        IsConnected = true;
        if (connection.TimeoutMs is int timeout)
            AcsCall.Run(() => _api.SetTimeout(timeout), "SetTimeout");
    }

    public void Disconnect()
    {
        if (!IsConnected)
            return;
        IsConnected = false;
        AcsCall.Run(_api.CloseComm, "CloseComm");
    }

    public ControllerInfo GetInfo()
    {
        EnsureConnected();
        var firmware = AcsCall.Run(_api.GetFirmwareVersion, "GetFirmwareVersion").Trim();
        var library = FormatVersion(AcsCall.Run(_api.GetLibraryVersion, "GetLibraryVersion"));

        // Every axis has one FPOS element, so the array length is the number of axes.
        var fpos = AcsCall.Run(() => _api.ReadVariableAsVector("FPOS", ProgramBuffer.ACSC_NONE, -1, -1, -1, -1), "Read FPOS");
        var axisCount = fpos is Array array ? array.Length : 1;

        return new ControllerInfo(firmware, library, axisCount);
    }

    public IAxis GetAxis(int index)
    {
        EnsureConnected();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new AcsAxis(_api, index);
    }

    public double ReadScalar(string variable, int index = -1)
    {
        EnsureConnected();
        var value = AcsCall.Run(() => _api.ReadVariableAsScalar(variable, ProgramBuffer.ACSC_NONE, index, -1), $"Read {variable}");
        return Convert.ToDouble(value);
    }

    public string Transaction(string command)
    {
        EnsureConnected();
        // The library returns null, not "", for commands without a reply (e.g. ENABLE).
        return AcsCall.Run(() => _api.Transaction(command), $"Transaction '{command}'") ?? string.Empty;
    }

    public void DisableAll()
    {
        EnsureConnected();
        AcsCall.Run(_api.DisableAll, "DisableAll");
    }

    public void KillAll()
    {
        EnsureConnected();
        AcsCall.Run(_api.KillAll, "KillAll");
    }

    public void Dispose()
    {
        try { Disconnect(); }
        catch (MotionControllerException) { /* Closing a dead channel is not worth failing Dispose. */ }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to the controller.");
    }

    // The library packs its version one byte per field, e.g. 0x030D0100 => 3.13.1.0.
    private static string FormatVersion(uint v) => $"{v >> 24}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";
}
