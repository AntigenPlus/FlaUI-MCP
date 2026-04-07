using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_click tool using the test apps.
/// </summary>
[Collection("TestApps")]
public class ClickTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClickTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task WinForms_Click_Button()
    {
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Click Me");
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.True(result.Contains("Clicked") || result.Contains("Invoked"),
            $"Expected Clicked or Invoked, got: {result}");
    }

    [Fact]
    public async Task WinForms_Click_Checkbox_Toggle()
    {
        var cbRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Enable the button below");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Result: {result}");
        // WinForms checkboxes support InvokePattern, so ClickTool prefers
        // Mouse.Click (Invoke fallback returns Invoked/Toggled).
        Assert.True(result.Contains("Clicked") || result.Contains("Invoked") || result.Contains("Toggled"),
            $"Expected Clicked, Invoked, or Toggled, got: {result}");

        // Toggle back to original state
        await _fixture.CallTool(tool, new { @ref = cbRef });
    }

    [Fact]
    public async Task WinForms_EnabledStateChanges_AfterCheckboxClick()
    {
        // Navigate to Buttons tab — other tests may have switched away.
        await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Enable the button below");

        // Take one snapshot and find both refs from it
        var snapshot1 = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        _output.WriteLine("Initial snapshot (partial):");
        foreach (var line in snapshot1.Split('\n'))
        {
            if (line.Contains("Conditional") || line.Contains("Enable"))
                _output.WriteLine(line);
        }

        var cbRef = TestAppFixture.FindRefInSnapshot(snapshot1, "Enable the button below");
        Assert.NotNull(cbRef);

        // Verify button is initially disabled
        Assert.Contains("disabled", snapshot1.Split('\n').First(l => l.Contains("Conditional Button")));

        // Click the checkbox to enable the button
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = cbRef });

        // Poll for the button to become enabled
        string snapshot2 = "";
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            snapshot2 = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
            var line = snapshot2.Split('\n').FirstOrDefault(l => l.Contains("Conditional Button"));
            if (line != null && !line.Contains("disabled"))
                break;
        }
        _output.WriteLine("\nAfter checkbox click:");
        foreach (var line in snapshot2.Split('\n'))
        {
            if (line.Contains("Conditional") || line.Contains("Enable"))
                _output.WriteLine(line);
        }

        var conditionalLine = snapshot2.Split('\n').First(l => l.Contains("Conditional Button"));
        _output.WriteLine($"\nConditional Button line: {conditionalLine}");

        // Button should now be enabled (no [disabled] tag)
        Assert.DoesNotContain("disabled", conditionalLine);

        // Clean up: uncheck the checkbox
        await _fixture.CallTool(clickTool, new { @ref = cbRef });
    }

    [Fact]
    public async Task WinForms_Click_ModalOpeningButton_DoesNotHangWorker()
    {
        // Regression test for issue #32: Invoke must not block the MCP worker thread.
        var modalBtnRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Dialogs", "Open Modal Dialog");

        var clickTool = new ClickTool(_fixture.Elements);

        try
        {
            // Race the click against a 2s timeout. Without the fix the WinForms
            // handler blocks indefinitely on ShowDialog(), so the click would
            // never return and the timeout branch would win.
            var clickTask = _fixture.CallTool(clickTool, new { @ref = modalBtnRef });
            var completed = await Task.WhenAny(clickTask, Task.Delay(2000));
            Assert.True(completed == clickTask,
                "windows_click did not return within 2s — Invoke is still blocking the worker thread (#32).");

            var result = await clickTask;
            _output.WriteLine($"Click result: {result}");
            Assert.True(result.Contains("Clicked") || result.Contains("Invoked"),
                $"Expected Clicked or Invoked, got: {result}");
        }
        finally
        {
            // Dismiss the modal via its AcceptButton (OK). The modal is focused
            // on open, so a global Enter press reaches it.
            await Task.Delay(500);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            await Task.Delay(500);
        }
    }

    [Fact]
    public async Task Wpf_Click_Button()
    {
        var buttonRef = _fixture.FindRefByName(_fixture.WpfHandle, "Click Me");
        Assert.NotNull(buttonRef);
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.True(result.Contains("Clicked") || result.Contains("Invoked"),
            $"Expected Clicked or Invoked, got: {result}");
    }

    [Fact]
    public async Task WinForms_Click_ModalOpen_SubsequentUiaCallsSucceed()
    {
        // Regression test for issue #32 — the deeper symptom. UIA Invoke is a
        // synchronous COM call that holds an RPC channel against the target
        // process for the entire duration of the invoked handler. For a button
        // that calls ShowDialog, the invoked handler doesn't return until the
        // modal closes — so even though the modal's message loop is pumping
        // and would normally serve UIA queries, the held COM channel makes
        // every subsequent UIA call into that process time out. The fix is
        // to use Mouse.Click instead of UIA Invoke when a clickable point is
        // available; mouse input is dispatched via Win32 SendInput and holds
        // no COM state.
        var modalBtnRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Dialogs", "Open Modal Dialog");

        var clickTool = new ClickTool(_fixture.Elements);

        try
        {
            var clickResult = await _fixture.CallTool(clickTool, new { @ref = modalBtnRef });
            _output.WriteLine($"Click result: {clickResult}");

            // Give the modal a moment to open and start pumping its loop.
            await Task.Delay(300);

            // Snapshot of the parent window while the modal is up. The parent's
            // UI thread is in the modal loop pumping messages, so UIA queries
            // must succeed promptly. Race against a 3s timeout — without the
            // mouse-click fix, this hangs at the UIA layer because the pending
            // Invoke COM call is holding the per-process RPC channel.
            var snapshotTask = Task.Run(() => _fixture.TakeSnapshot(_fixture.WinFormsHandle));
            var winner = await Task.WhenAny(snapshotTask, Task.Delay(3000));
            Assert.True(winner == snapshotTask,
                "Snapshot did not return within 3s while modal was open — pending UIA Invoke is holding the COM channel against the AUT process (#32).");

            var snapshot = await snapshotTask;
            // The snapshot should at least include the parent window's title.
            Assert.Contains("FlaUI-MCP Test App", snapshot);
        }
        finally
        {
            // Dismiss the modal via its AcceptButton (OK).
            await Task.Delay(200);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            await Task.Delay(500);
        }
    }
}
