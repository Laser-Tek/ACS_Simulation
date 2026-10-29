namespace AcsHal.Core;

/// <summary>A controller call failed. <see cref="ErrorCode"/> is the vendor error code when one is available.</summary>
public class MotionControllerException : Exception
{
    public int? ErrorCode { get; }

    public MotionControllerException(string message, int? errorCode = null, Exception? inner = null)
        : base(errorCode is null ? message : $"{message} (error {errorCode})", inner)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>An axis faulted or was disabled while a caller was waiting on it.</summary>
public sealed class AxisFaultException(AxisStatus status, string message)
    : MotionControllerException($"Axis {status.Index}: {message}. Faults: {status.Faults}")
{
    public AxisStatus Status { get; } = status;
}
