using ACS.SPiiPlusNET;
using AcsHal.Core;

namespace AcsHal.Acs;

/// <summary>
/// Runs ACS library calls one at a time and turns <see cref="ACSException"/> into
/// <see cref="MotionControllerException"/>. The library does not document thread safety, so every call
/// on a channel goes through that channel's lock (e.g. UI commands interleaved with status polling).
/// </summary>
internal sealed class AcsCall
{
    private readonly Lock _lock = new();

    public T Run<T>(Func<T> func, string operation)
    {
        lock (_lock)
        {
            try
            {
                return func();
            }
            catch (ACSException ex)
            {
                throw new MotionControllerException($"{operation} failed: {ex.Message.Trim()}", ex.ErrorCode, ex);
            }
        }
    }

    public void Run(Action action, string operation) =>
        Run(() => { action(); return 0; }, operation);
}
