using System.Diagnostics;
using ACS.SPiiPlusNET;
using AcsHal.Core;

namespace AcsHal.Acs;

internal sealed class AcsAxis(Api api, AcsCall call, int index) : IAxis
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly Axis _axis = (Axis)index;

    public int Index { get; } = index;

    public AxisStatus GetStatus()
    {
        var state = Call(() => api.GetMotorState(_axis), "GetMotorState");
        return new AxisStatus(
            Index,
            FeedbackPosition: Call(() => api.GetFPosition(_axis), "GetFPosition"),
            ReferencePosition: Call(() => api.GetRPosition(_axis), "GetRPosition"),
            FeedbackVelocity: Call(() => api.GetFVelocity(_axis), "GetFVelocity"),
            Enabled: state.HasFlag(MotorStates.ACSC_MST_ENABLE),
            Moving: state.HasFlag(MotorStates.ACSC_MST_MOVE),
            InPosition: state.HasFlag(MotorStates.ACSC_MST_INPOS),
            Faults: (AxisFaults)(uint)Call(() => api.GetFault(_axis), "GetFault"));
    }

    public void Enable()
    {
        // Simulator 3.13.01 quirk: the first ENABLE of a true-gantry pair often enables only the yaw
        // motor and fails with 3065 ("motion in progress"); another ENABLE ~100 ms later completes it.
        // ACS's own gantry example uses a single ENABLE, so hardware is not expected to hit the retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Call(() => api.Enable(_axis), "Enable");
                return;
            }
            catch (MotionControllerException ex) when (ex.ErrorCode == 3065 && attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }
    public void Disable() => Call(() => api.Disable(_axis), "Disable");
    public void ClearFaults() => Call(() => api.FaultClear(_axis), "FaultClear");

    public void SetMotionProfile(MotionProfile profile)
    {
        if (profile.Velocity is double vel) Call(() => api.SetVelocity(_axis, vel), "SetVelocity");
        if (profile.Acceleration is double acc) Call(() => api.SetAcceleration(_axis, acc), "SetAcceleration");
        if (profile.Deceleration is double dec) Call(() => api.SetDeceleration(_axis, dec), "SetDeceleration");
        if (profile.Jerk is double jerk) Call(() => api.SetJerk(_axis, jerk), "SetJerk");
        if (profile.KillDeceleration is double kdec) Call(() => api.SetKillDeceleration(_axis, kdec), "SetKillDeceleration");
    }

    public double GetUnitsPerCount() =>
        Convert.ToDouble(Call(() => api.ReadVariableAsScalar("EFAC", ProgramBuffer.ACSC_NONE, Index, -1), "Read EFAC"));

    // The controller rejects EFAC changes while the motor is enabled (error 3078).
    public void SetUnitsPerCount(double unitsPerCount) =>
        Call(() => api.WriteVariable(unitsPerCount, "EFAC", ProgramBuffer.ACSC_NONE, Index, Index, -1, -1), "Write EFAC");

    public void MoveAbsolute(double position) =>
        Call(() => api.ToPoint(MotionFlags.ACSC_NONE, _axis, position), "ToPoint");

    public void MoveRelative(double distance) =>
        Call(() => api.ToPoint(MotionFlags.ACSC_AMF_RELATIVE, _axis, distance), "ToPoint (relative)");

    // ACSC_AMF_VELOCITY makes the controller use the given velocity instead of the axis default.
    public void Jog(double velocity) =>
        Call(() => api.Jog(MotionFlags.ACSC_AMF_VELOCITY, _axis, velocity), "Jog");

    public void Halt() => Call(() => api.Halt(_axis), "Halt");
    public void Kill() => Call(() => api.Kill(_axis), "Kill");

    public Task WaitEnabledAsync(bool enabled, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        PollUntilAsync(s => s.Enabled == enabled, $"to become {(enabled ? "enabled" : "disabled")}", timeout, cancellationToken);

    public Task WaitMotionDoneAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        PollUntilAsync(s =>
        {
            if (!s.Enabled)
                throw new AxisFaultException(s, "disabled during motion");
            return !s.Moving;
        }, "to finish moving", timeout, cancellationToken);

    // Polling rather than the library's blocking waits, so callers can cancel and nothing ties up a thread.
    private async Task PollUntilAsync(Func<AxisStatus, bool> done, string what, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var status = GetStatus();
            if (done(status))
                return;
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException($"Axis {Index} did not {what} within {timeout.TotalSeconds:0.###} s. Last status: {status}");
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private T Call<T>(Func<T> func, string operation) => call.Run(func, $"Axis {Index}: {operation}");
    private void Call(Action action, string operation) => call.Run(action, $"Axis {Index}: {operation}");
}
