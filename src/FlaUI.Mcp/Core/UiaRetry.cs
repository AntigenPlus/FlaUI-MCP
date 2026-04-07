using System.Runtime.InteropServices;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Transparent retry for transient UIA COM exceptions (#38). Use on
/// UIA-reading tool bodies. Do NOT use on side-effect tools (click,
/// type, etc.) — repeating an action could fire it more than once.
/// </summary>
public static class UiaRetry
{
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    private const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
    private const int E_EVENT_NO_SUBSCRIBERS = unchecked((int)0x80040201);

    /// <summary>
    /// Backoff schedule between retries, in milliseconds. Length determines
    /// the maximum retry count (attempts = BackoffMs.Length + 1).
    /// </summary>
    private static readonly int[] BackoffMs = { 100, 200, 400 };

    private static readonly HashSet<int> RetryableHResults = new()
    {
        E_UNEXPECTED,
        RPC_E_DISCONNECTED,
        RPC_E_CALL_REJECTED,
        E_EVENT_NO_SUBSCRIBERS,
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
