using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_find tool using the test apps.
/// </summary>
[Collection("TestApps")]
public class FindTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FindTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Find_ByRole_ReturnsMatchingElements()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, role = "button", depth = 5 });
        _output.WriteLine(result);

        Assert.Contains("matching element", result);
        Assert.Contains("Click Me", result);
    }

    [Fact]
    public async Task Find_ByName_SubstringMatch()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, name = "Conditional" });
        _output.WriteLine(result);

        Assert.Contains("Conditional Button", result);
    }

    [Fact]
    public async Task Find_ByNameAndRole_NarrowsResults()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);

        // Find all buttons
        var allButtons = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, role = "button", depth = 5 });
        var allCount = allButtons.Split('\n').Count(l => l.TrimStart().StartsWith("- "));

        // Find buttons containing "Click"
        var clickButtons = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, name = "Click", role = "button" });
        var clickCount = clickButtons.Split('\n').Count(l => l.TrimStart().StartsWith("- "));

        _output.WriteLine($"All buttons: {allCount}, 'Click' buttons: {clickCount}");
        Assert.True(clickCount < allCount, "Name filter should narrow results");
        Assert.True(clickCount >= 1, "Should find at least one 'Click' button");
    }

    [Fact]
    public async Task Find_ByRole_Checkbox()
    {
        // Navigate to Buttons tab first
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, role = "checkbox" });
        _output.WriteLine(result);

        Assert.Contains("Enable the button below", result);
    }

    [Fact]
    public async Task Find_ByPattern_Toggle()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, pattern = "toggle" });
        _output.WriteLine(result);

        // Checkboxes support toggle pattern
        Assert.Contains("matching element", result);
    }

    [Fact]
    public async Task Find_ByDepth_LimitsTraversal()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);

        // Depth 1: only immediate children of the window
        var shallow = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, role = "button", depth = 1 });
        var shallowCount = shallow.Split('\n').Count(l => l.TrimStart().StartsWith("- "));

        // Depth 10: full tree
        var deep = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, role = "button", depth = 10 });
        var deepCount = deep.Split('\n').Count(l => l.TrimStart().StartsWith("- "));

        _output.WriteLine($"Depth 1: {shallowCount} buttons, Depth 10: {deepCount} buttons");
        Assert.True(deepCount >= shallowCount, "Deeper search should find at least as many elements");
    }

    [Fact]
    public async Task Find_NoMatches_ReturnsMessage()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, name = "This Element Does Not Exist XYZ" });
        _output.WriteLine(result);

        Assert.Contains("No matching elements", result);
    }

    [Fact]
    public async Task Find_RefsWorkWithOtherTools()
    {
        // Find a button, then click it using the returned ref
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle, name = "Click Me", role = "button" });
        _output.WriteLine($"Find result: {result}");

        // Extract ref from result. The annotation is [ref=<refid>] or
        // [ref=<refid>, id=<automationId>] — accept either.
        var refMatch = System.Text.RegularExpressions.Regex.Match(result, @"\[ref=(\w+)(?:,|\])");
        Assert.True(refMatch.Success, "Should contain a ref");
        var foundRef = refMatch.Groups[1].Value;
        _output.WriteLine($"Extracted ref: {foundRef}");

        // Click it
        var clickTool = new ClickTool(_fixture.Elements);
        var clickResult = await _fixture.CallTool(clickTool, new { @ref = foundRef });
        _output.WriteLine($"Click result: {clickResult}");
        Assert.Contains("Clicked", clickResult);
    }

    [Fact]
    public async Task Find_Wpf_ByRole()
    {
        var tool = new FindTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WpfHandle, role = "button", depth = 5 });
        _output.WriteLine(result);

        Assert.Contains("Click Me", result);
    }
}
