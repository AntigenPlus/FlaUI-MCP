using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_click — physical mouse click via Mouse.Click. See
/// InvokeTests for the windows_invoke (UIA pattern dispatch) counterpart.
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
    public async Task WinForms_Click_Button_DispatchesMouseClick()
    {
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Click Me");
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Clicked", result);
    }

    [Fact]
    public async Task Wpf_Click_Button_DispatchesMouseClick()
    {
        var buttonRef = await _fixture.NavigateToTabAndFind(
            _fixture.WpfHandle, "Buttons", "Click Me");
        _output.WriteLine($"Click Me button ref: {buttonRef}");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Clicked", result);
    }

    [Fact]
    public async Task WinForms_Click_GridCheckbox_AutoDoubleClicks()
    {
        // WinForms DataGridView checkboxes need a mouse double-click to fire
        // CellContentClick + EndEdit (so the underlying data model updates).
        // ClickTool detects grid checkboxes via IsGridCheckbox and auto-promotes
        // the single click to a double-click. Verify the result message reflects
        // that promotion.
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Grid");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        // Poll for grid checkbox to appear
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? cbRef = null;
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            cbRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Select Row 0");
            if (cbRef != null) break;
        }
        Assert.NotNull(cbRef);

        var result = await _fixture.CallTool(clickTool, new { @ref = cbRef });
        _output.WriteLine($"Grid checkbox click: {result}");
        Assert.Contains("Double-clicked", result);
    }

    [Fact]
    public async Task WinForms_Click_Checkbox_TogglesIt()
    {
        var cbRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Enable the button below");

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Clicked", result);

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
    public async Task WinForms_Click_WithCtrlModifier_HoldsKeyDuringClick()
    {
        // Issue #34: windows_click must support modifier keys so agents can
        // drive Ctrl-click multi-select in grids/lists. The test app's
        // Modifier Test Button captures Control.ModifierKeys when its Click
        // handler fires and writes the result into a label.
        await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Modifier Test Button");

        var clickTool = new ClickTool(_fixture.Elements);

        // Baseline: click with no modifier — label should read "None".
        var btnRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Modifier Test Button");
        Assert.NotNull(btnRef);
        await _fixture.CallTool(clickTool, new { @ref = btnRef });
        await Task.Delay(100);

        var snapshotAfterPlain = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var plainLine = snapshotAfterPlain.Split('\n').FirstOrDefault(l => l.Contains("Last click modifiers"));
        _output.WriteLine($"After plain click: {plainLine}");
        Assert.NotNull(plainLine);
        Assert.Contains("None", plainLine);

        // Ctrl+click: label should reflect Control.
        var btnRef2 = _fixture.FindRefByName(_fixture.WinFormsHandle, "Modifier Test Button");
        Assert.NotNull(btnRef2);
        var result = await _fixture.CallTool(clickTool, new
        {
            @ref = btnRef2,
            modifiers = new[] { "ctrl" }
        });
        _output.WriteLine($"Click result: {result}");
        Assert.Contains("[ctrl]", result);
        await Task.Delay(100);

        var snapshotAfterCtrl = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var ctrlLine = snapshotAfterCtrl.Split('\n').FirstOrDefault(l => l.Contains("Last click modifiers"));
        _output.WriteLine($"After ctrl-click: {ctrlLine}");
        Assert.NotNull(ctrlLine);
        Assert.Contains("Control", ctrlLine);
    }

    [Fact]
    public async Task WinForms_Click_UnknownModifier_ReturnsError()
    {
        var btnRef = await _fixture.NavigateToTabAndFind(
            _fixture.WinFormsHandle, "Buttons", "Click Me");

        var clickTool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(clickTool, new
        {
            @ref = btnRef,
            modifiers = new[] { "hyper" }
        });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Unknown modifier", result);
    }
}
