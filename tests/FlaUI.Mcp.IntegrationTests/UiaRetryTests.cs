using System.Runtime.InteropServices;
using PlaywrightWindows.Mcp.Core;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Unit tests for UiaRetry. No app dependency — these are pure
/// behavioral tests of the retry policy (#38).
/// </summary>
public class UiaRetryTests
{
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    private const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
    private const int E_INVALIDARG = unchecked((int)0x80070057); // not retryable

    [Fact]
    public void With_FirstAttemptSucceeds_NoRetry()
    {
        int callCount = 0;
        var result = UiaRetry.With(() => { callCount++; return "ok"; });

        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void With_RetryableThenSuccess_ReturnsValue()
    {
        int callCount = 0;
        var result = UiaRetry.With(() =>
        {
            callCount++;
            if (callCount < 3) throw new COMException("transient", E_UNEXPECTED);
            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void With_AllAttemptsFailWithRetryable_Throws()
    {
        int callCount = 0;
        var ex = Assert.Throws<COMException>(() => UiaRetry.With<int>(() =>
        {
            callCount++;
            throw new COMException("still transient", E_UNEXPECTED);
        }));

        Assert.Equal(E_UNEXPECTED, ex.HResult);
        // Default backoff schedule is {100, 200, 400} ms => 4 total attempts.
        Assert.Equal(4, callCount);
    }

    [Fact]
    public void With_NonRetryableException_ThrowsImmediately()
    {
        int callCount = 0;
        Assert.Throws<COMException>(() => UiaRetry.With<int>(() =>
        {
            callCount++;
            throw new COMException("real error", E_INVALIDARG);
        }));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void With_NonComException_ThrowsImmediately()
    {
        int callCount = 0;
        Assert.Throws<InvalidOperationException>(() => UiaRetry.With<int>(() =>
        {
            callCount++;
            throw new InvalidOperationException("not a COM error");
        }));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void With_RpcDisconnected_IsRetryable()
    {
        int callCount = 0;
        var result = UiaRetry.With(() =>
        {
            callCount++;
            if (callCount == 1) throw new COMException("rpc blip", RPC_E_DISCONNECTED);
            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void With_VoidOverload_RetriesAndReturns()
    {
        int callCount = 0;
        UiaRetry.With(() =>
        {
            callCount++;
            if (callCount < 2) throw new COMException("transient", E_UNEXPECTED);
        });

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void IsRetryableHResult_KnownCodes_True()
    {
        Assert.True(UiaRetry.IsRetryableHResult(E_UNEXPECTED));
        Assert.True(UiaRetry.IsRetryableHResult(RPC_E_DISCONNECTED));
        Assert.True(UiaRetry.IsRetryableHResult(unchecked((int)0x80010001))); // RPC_E_CALL_REJECTED
        Assert.True(UiaRetry.IsRetryableHResult(unchecked((int)0x80040201)));
    }

    [Fact]
    public void IsRetryableHResult_UnknownCode_False()
    {
        Assert.False(UiaRetry.IsRetryableHResult(E_INVALIDARG));
        Assert.False(UiaRetry.IsRetryableHResult(0));
        Assert.False(UiaRetry.IsRetryableHResult(unchecked((int)0x80070005))); // access denied
    }
}
