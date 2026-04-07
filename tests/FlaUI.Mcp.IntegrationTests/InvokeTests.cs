using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_invoke — UIA pattern dispatch (Invoke / Toggle /
/// SelectionItem). See ClickTests for the windows_click (physical mouse
/// click) counterpart.
/// </summary>
[Collection("TestApps")]
public class InvokeTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InvokeTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task WinForms_Invoke_Button_TriggersInvokePattern()
    {
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Click Me");
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new InvokeTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("UIA Invoke", result);
        Assert.Contains("invoked Click Me", result);
    }

    [Fact]
    public async Task Wpf_Invoke_Button_TriggersInvokePattern()
    {
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WpfHandle, "Buttons", "Click Me");

        var tool = new InvokeTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("UIA Invoke", result);
    }

    [Fact]
    public async Task WinForms_Invoke_ModalOpeningButton_ReturnsHangWarning()
    {
        // Issue #32: UIA Invoke is a synchronous COM call that holds an RPC
        // channel against the target process for the duration of the invoked
        // handler. For a button that calls ShowDialog, the handler doesn't
        // return until the modal is dismissed — so subsequent UIA tools
        // against the AUT would queue behind the held channel and time out.
        //
        // The fix dispatches Invoke onto a background task, races it against
        // a 500ms timeout, and returns a WARNING if the handler hasn't
        // returned. The warning text names windows_click as the alternative.
        var modalBtnRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Dialogs", "Open Modal Dialog");

        var invokeTool = new InvokeTool(_fixture.Elements);

        try
        {
            // The call must return promptly even though the WinForms handler
            // is blocked on ShowDialog().
            var invokeTask = _fixture.CallTool(invokeTool, new { @ref = modalBtnRef });
            var completed = await Task.WhenAny(invokeTask, Task.Delay(3000));
            Assert.True(completed == invokeTask,
                "windows_invoke did not return within 3s — Invoke is still blocking the worker thread (#32).");

            var result = await invokeTask;
            _output.WriteLine($"Invoke result: {result}");
            Assert.Contains("invoked Open Modal Dialog", result);
            Assert.Contains("WARNING", result);
            Assert.Contains("windows_click", result);
        }
        finally
        {
            // Dismiss the modal via its AcceptButton (OK). The modal is
            // focused on open, so a global Enter press reaches it.
            await Task.Delay(500);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            await Task.Delay(500);
        }
    }
}
