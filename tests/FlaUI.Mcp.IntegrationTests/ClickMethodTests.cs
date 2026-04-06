using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for the method parameter on windows_click tool.
/// </summary>
[Collection("TestApps")]
public class ClickMethodTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClickMethodTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private async Task<string> GetButtonRef()
    {
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var buttonRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Click Me");
        Assert.NotNull(buttonRef);
        return buttonRef;
    }

    [Fact]
    public async Task Click_MethodAuto_UsesInvokeForButton()
    {
        var buttonRef = await GetButtonRef();

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef, method = "auto" });
        _output.WriteLine($"Result: {result}");

        // Auto should prefer Invoke for a standard button
        Assert.Contains("Invoked", result);
    }

    [Fact]
    public async Task Click_MethodInvoke_UsesInvokePattern()
    {
        var buttonRef = await GetButtonRef();

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef, method = "invoke" });
        _output.WriteLine($"Result: {result}");

        Assert.Contains("Invoked", result);
    }

    [Fact]
    public async Task Click_MethodMouse_UsesPhysicalClick()
    {
        var buttonRef = await GetButtonRef();

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef, method = "mouse" });
        _output.WriteLine($"Result: {result}");

        // Mouse method should report "Clicked" not "Invoked"
        Assert.Contains("Clicked", result);
    }

    [Fact]
    public async Task Click_DefaultMethod_SameAsAuto()
    {
        var buttonRef = await GetButtonRef();

        var tool = new ClickTool(_fixture.Elements);

        // No method specified
        var result1 = await _fixture.CallTool(tool, new { @ref = buttonRef });
        // Explicit auto
        var result2 = await _fixture.CallTool(tool, new { @ref = buttonRef, method = "auto" });

        _output.WriteLine($"Default: {result1}");
        _output.WriteLine($"Auto: {result2}");

        // Both should use the same pattern (Invoke for a button)
        Assert.Equal(result1.Contains("Invoked"), result2.Contains("Invoked"));
    }

    [Fact]
    public async Task Click_MethodMouse_Wpf()
    {
        var buttonRef = _fixture.FindRefByName(_fixture.WpfHandle, "Click Me");
        Assert.NotNull(buttonRef);

        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef, method = "mouse" });
        _output.WriteLine($"WPF mouse click: {result}");

        Assert.Contains("Clicked", result);
    }

    [Fact]
    public async Task Click_Auto_GridCheckbox_UsesDoubleClick()
    {
        // Navigate to Grid tab
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

        // Click with auto method — should use double-click for grid checkbox
        var tool = new ClickTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Grid checkbox auto click: {result}");

        // Should use mouse click (not Invoke) because it's a grid checkbox.
        // The exact click behavior (single vs double) depends on the DataGridView's
        // commit pattern — some apps use CellContentClick+EndEdit (needs double-click),
        // others use CurrentCellDirtyStateChanged+CommitEdit (single click suffices).
        // Auto mode uses double-click as the more universally compatible approach.
        Assert.Contains("Double-clicked", result);
        Assert.DoesNotContain("Invoked", result);
    }
}
