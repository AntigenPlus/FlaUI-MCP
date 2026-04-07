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
    public async Task WinForms_Click_Button_Invoke()
    {
        var buttonRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Click Me");
        Assert.NotNull(buttonRef);
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Invoked", result);
    }

    [Fact]
    public async Task WinForms_Click_Checkbox_Toggle()
    {
        var cbRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Enable the button below");
        Assert.NotNull(cbRef);

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Result: {result}");
        // WinForms checkboxes support InvokePattern, so ClickTool uses Invoke (not Toggle)
        Assert.True(result.Contains("Invoked") || result.Contains("Toggled"),
            $"Expected Invoked or Toggled, got: {result}");

        // Toggle back to original state
        await _fixture.CallTool(tool, new { @ref = cbRef });
    }

    [Fact]
    public async Task WinForms_EnabledStateChanges_AfterCheckboxClick()
    {
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
        // Regression test for issue #32: clicking a button whose handler calls
        // ShowDialog() used to block the MCP worker thread for as long as the modal
        // stayed open, because Patterns.Invoke.Pattern.Invoke() blocks until the
        // invoked handler returns. The fix dispatches Invoke onto a background
        // thread and returns immediately.

        // Navigate to the Dialogs tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var dialogsTabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Dialogs");
        Assert.NotNull(dialogsTabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = dialogsTabRef });
        await Task.Delay(200);

        // Find the "Open Modal Dialog" button on the Dialogs tab
        var modalBtnRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Open Modal Dialog");
        Assert.NotNull(modalBtnRef);
        _output.WriteLine($"Open Modal Dialog ref: {modalBtnRef}");

        try
        {
            // The click call must return promptly even though the WinForms handler
            // will block on ShowDialog(). Race against a generous timeout — without
            // the fix, the call hangs until the dialog is dismissed externally.
            // We measure elapsed time too so a regression manifests as a clear
            // duration failure rather than just a timeout.
            var sw = Stopwatch.StartNew();
            var clickTask = _fixture.CallTool(clickTool, new { @ref = modalBtnRef });
            var completed = await Task.WhenAny(clickTask, Task.Delay(5000));
            sw.Stop();
            Assert.True(completed == clickTask,
                "windows_click did not return within 5s — Invoke is still blocking the worker thread (issue #32).");

            var result = await clickTask;
            _output.WriteLine($"Click returned in {sw.ElapsedMilliseconds}ms with: {result}");
            Assert.Contains("Invoked", result);

            // Sanity check: should be well under a second when fire-and-forget works.
            // Without the fix this would hang for the full 5s and fail above.
            Assert.True(sw.ElapsedMilliseconds < 2000,
                $"windows_click took {sw.ElapsedMilliseconds}ms; expected near-instant return.");
        }
        finally
        {
            // Dismiss the modal so subsequent tests find the parent window usable.
            // The dialog's AcceptButton is OK, so Enter dismisses it. Modal is
            // focused on open, so a global keyboard press goes to it.
            await Task.Delay(500); // give the modal time to actually open
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            await Task.Delay(500);
        }
    }

    [Fact]
    public async Task Wpf_Click_Button_Invoke()
    {
        var buttonRef = _fixture.FindRefByName(_fixture.WpfHandle, "Click Me");
        Assert.NotNull(buttonRef);
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Invoked", result);
    }
}
