using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_press_key tool using the test apps.
/// </summary>
[Collection("TestApps")]
public class PressKeyTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PressKeyTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task PressKey_SingleKey()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "Tab" });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Pressed", result);
        Assert.Contains("Tab", result);
    }

    [Fact]
    public async Task PressKey_WithModifier()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "a", modifiers = new[] { "ctrl" } });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Pressed", result);
        Assert.Contains("CTRL", result);
    }

    [Fact]
    public async Task PressKey_UnknownKey_ReturnsError()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "nonexistent_key" });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Unknown key", result);
    }

    [Fact]
    public async Task PressKey_UnknownModifier_ReturnsError()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "a", modifiers = new[] { "super" } });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Unknown modifier", result);
    }

    [Fact]
    public async Task PressKey_WithRef_FocusesElement()
    {
        // Navigate to Forms tab and find the Name text box
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Forms");
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        // Poll for the Name text box to appear
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? nameRef = null;
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            nameRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Name");
            if (nameRef != null) break;
        }
        Assert.NotNull(nameRef);

        // Clear the field first
        var fillTool = new FillTool(_fixture.Elements);
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "" });

        // Press 'a' with ref to focus the Name text box
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "a", @ref = nameRef });
        _output.WriteLine($"Press result: {result}");
        Assert.Contains("focused", result);

        // Verify the character was typed
        var textTool = new GetTextTool(_fixture.Elements);
        var text = await _fixture.CallTool(textTool, new { @ref = nameRef });
        _output.WriteLine($"Text after press: '{text}'");
        Assert.Equal("a", text);

        // Clean up
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "" });
    }

    [Fact]
    public async Task PressKey_Escape()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "Escape" });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Pressed", result);
        Assert.Contains("Escape", result);
    }

    [Fact]
    public async Task PressKey_FunctionKey()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "F5" });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Pressed", result);
        Assert.Contains("F5", result);
    }

    [Fact]
    public async Task PressKey_MultipleModifiers()
    {
        var tool = new PressKeyTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { key = "s", modifiers = new[] { "ctrl", "shift" } });
        _output.WriteLine($"Result: {result}");
        Assert.Contains("Pressed", result);
        Assert.Contains("CTRL", result);
        Assert.Contains("SHIFT", result);
    }
}
