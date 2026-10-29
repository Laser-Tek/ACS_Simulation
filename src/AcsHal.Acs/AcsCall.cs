using ACS.SPiiPlusNET;
using AcsHal.Core;

namespace AcsHal.Acs;

/// <summary>Runs ACS library calls and turns <see cref="ACSException"/> into <see cref="MotionControllerException"/>.</summary>
internal static class AcsCall
{
    public static T Run<T>(Func<T> func, string operation)
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

    public static void Run(Action action, string operation) =>
        Run(() => { action(); return 0; }, operation);
}
