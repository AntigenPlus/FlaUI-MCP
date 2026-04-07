using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_dump_ids — diagnostic dump of AutomationIds in a
/// window/subtree (#35).
/// </summary>
[Collection("TestApps")]
public class DumpIdsTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DumpIdsTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task DumpIds_WinFormsWindow_ListsKnownControls()
    {
        // Make sure we're on the Buttons tab so the button controls are realized.
        await _fixture.NavigateToTabAndFind(_fixture.WinFormsHandle, "Buttons", "Click Me");

        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WinFormsHandle });
        _output.WriteLine(result);

        // Header row + at least the buttons-tab named controls.
        Assert.Contains("AutomationId", result);
        Assert.Contains("ClickMeButton", result);
        Assert.Contains("EnableCheckbox", result);
        Assert.Contains("ConditionalButton", result);
        Assert.Contains("RadioA", result);
    }

    [Fact]
    public async Task DumpIds_FilterRegex_NarrowsResults()
    {
        await _fixture.NavigateToTabAndFind(_fixture.WinFormsHandle, "Buttons", "Click Me");

        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new
        {
            handle = _fixture.WinFormsHandle,
            filter = "^Radio"
        });
        _output.WriteLine(result);

        Assert.Contains("RadioA", result);
        Assert.Contains("RadioB", result);
        Assert.Contains("RadioC", result);
        // Filter should exclude things that don't start with "Radio"
        Assert.DoesNotContain("ClickMeButton", result);
        Assert.DoesNotContain("EnableCheckbox", result);
    }

    [Fact]
    public async Task DumpIds_FilterRegex_NoMatches_ReturnsHelpfulMessage()
    {
        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new
        {
            handle = _fixture.WinFormsHandle,
            filter = "ZzZNoSuchThingZzZ"
        });
        _output.WriteLine(result);
        Assert.Contains("No descendants", result);
    }

    [Fact]
    public async Task DumpIds_NoHandleOrRef_ReturnsError()
    {
        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { });
        _output.WriteLine(result);
        Assert.Contains("Specify", result);
    }

    [Fact]
    public async Task DumpIds_InvalidRegex_ReturnsError()
    {
        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new
        {
            handle = _fixture.WinFormsHandle,
            filter = "[unclosed"
        });
        _output.WriteLine(result);
        Assert.Contains("Invalid filter regex", result);
    }

    [Fact]
    public async Task DumpIds_WpfWindow_ListsKnownControls()
    {
        // WPF test app sets AutomationProperties.AutomationId on all named controls.
        await _fixture.NavigateToTabAndFind(_fixture.WpfHandle, "Buttons", "Click Me");

        var tool = new DumpIdsTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { handle = _fixture.WpfHandle });
        _output.WriteLine(result);

        Assert.Contains("ClickMeButton", result);
        Assert.Contains("EnableCheckbox", result);
        Assert.Contains("RadioA", result);
    }
}
