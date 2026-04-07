using System.Runtime.InteropServices;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Transparent retry for transient UIA COM exceptions.
///
/// UIA FindFirst/FindAll operations against WPF DataGrid (and occasionally
/// other virtualized WPF controls) can throw COMException with HRESULT
/// E_UNEXPECTED while the control is updating its UIA tree. The exception
/// looks catastrophic but almost always clears within a few hundred ms,
/// and a retry succeeds. Without transparent retry every MCP caller has
/// to reimplement the same backoff loop, and LLM agents in particular
/// tend to mis-diagnose these as "the element is gone" and start hunting
/// for an alternative path that wastes tokens (#38).
///
/// Wrap UIA-reading tool bodies in <see cref="With{T}"/> / <see cref="With"/>.
/// Tools with side effects (click, type, focus, etc.) should NOT use this
/// helper — retrying a side-effect call could cause the action to happen
/// more than once.
/// </summary>
public static class UiaRetry
{
    /// <summary>
    /// Backoff schedule between retries, in milliseconds. Length determines
    /// the maximum retry count (i.e. attempts = BackoffMs.Length + 1).
    /// </summary>
    private static readonly int[] BackoffMs = { 100, 200, 400 };

    private static readonly HashSet<int> RetryableHResults = new()
    {
        unchecked((int)0x8000FFFF), // E_UNEXPECTED — UIA tree mid-update
        unchecked((int)0x80010108), // RPC_E_DISCONNECTED — UIA RPC channel blipped
        unchecked((int)0x80010001), // RPC_E_CALL_REJECTED — UIA peer was busy
        unchecked((int)0x80040201), // "Event was unable to invoke any of the subscribers"
    };

    public static T With<T>(Func<T> action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (RetryableHResults.Contains(ex.HResult))
            {
                if (attempt >= BackoffMs.Length) throw;
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }

    public static void With(Action action)
    {
        With<bool>(() => { action(); return true; });
    }

    /// <summary>
    /// True if the HRESULT matches one of the transient UIA error codes that
    /// <see cref="With{T}"/> retries. Exposed so callers (e.g. WaitTool's
    /// poll loop) can decide for themselves whether to swallow an exception.
    /// </summary>
    public static bool IsRetryableHResult(int hResult) => RetryableHResults.Contains(hResult);
}
