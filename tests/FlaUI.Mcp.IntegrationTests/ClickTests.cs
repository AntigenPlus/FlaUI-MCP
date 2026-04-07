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
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Click Me");
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Invoked", result);
    }

    [Fact]
    public async Task WinForms_Click_Checkbox_Toggle()
    {
        var cbRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Enable the button below");

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

    [Fact]
    public async Task WinForms_Click_ModalOpeningButton_ReturnsWarning()
    {
        // Regression test for issue #32. UIA Invoke is a synchronous COM call
        // that holds a per-process RPC channel for the duration of the invoked
        // handler. For a button that opens a modal via ShowDialog, the handler
        // doesn't return until the modal is dismissed — so subsequent UIA tools
        // against the AUT would queue behind the held channel and time out.
        //
        // The fix dispatches Invoke onto a background task, races it against
        // a short timeout, and returns a WARNING if it doesn't complete. The
        // caller is told what happened and which FlaUI primitive to use as a
        // workaround. The background Invoke is left running and completes when
        // the modal is dismissed.
        var modalBtnRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Dialogs", "Open Modal Dialog");

        var clickTool = new ClickTool(_fixture.Elements);

        try
        {
            // The click call must return promptly even though the WinForms
            // handler is blocked on ShowDialog().
            var clickTask = _fixture.CallTool(clickTool, new { @ref = modalBtnRef });
            var completed = await Task.WhenAny(clickTask, Task.Delay(3000));
            Assert.True(completed == clickTask,
                "windows_click did not return within 3s — Invoke is still blocking the worker thread (#32).");

            var result = await clickTask;
            _output.WriteLine($"Click result: {result}");
            Assert.Contains("Invoked Open Modal Dialog", result);
            Assert.Contains("WARNING", result);
            Assert.Contains("Mouse.Click", result);
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
}
