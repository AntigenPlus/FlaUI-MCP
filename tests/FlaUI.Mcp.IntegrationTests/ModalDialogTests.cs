using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for modal dialog discovery via windows_list_windows.
/// Uses the WinForms test app's Dialogs tab which has modal and modeless dialog launchers.
/// </summary>
[Collection("TestApps")]
public class ModalDialogTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ModalDialogTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ListWindows_ShowsModelessChildDialog()
    {
        // Navigate to Dialogs tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Dialogs");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        // Poll for the modeless dialog button
        var sw = Stopwatch.StartNew();
        string? buttonRef = null;
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            buttonRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Open Modeless Dialog");
            if (buttonRef != null) break;
        }
        Assert.NotNull(buttonRef);

        // Open the modeless dialog
        await _fixture.CallTool(clickTool, new { @ref = buttonRef });
        await Task.Delay(500);

        // List windows — should include the child dialog
        var windows = _fixture.Session.ListWindows();
        _output.WriteLine("Windows found:");
        foreach (var (handle, title, process) in windows)
            _output.WriteLine($"  {handle}: \"{title}\" ({process})");

        var modelessDialog = windows.FirstOrDefault(w => w.title.Contains("Test Modeless Dialog"));
        Assert.NotNull(modelessDialog.handle);
        _output.WriteLine($"Found modeless dialog: {modelessDialog.handle}");

        // Clean up: close the dialog
        var dialogWindow = _fixture.Session.GetWindow(modelessDialog.handle);
        if (dialogWindow != null)
        {
            var closeRef = _fixture.FindRefByName(modelessDialog.handle, "Close");
            if (closeRef != null)
                await _fixture.CallTool(clickTool, new { @ref = closeRef });
            else
                dialogWindow.Close();
        }
    }
}
