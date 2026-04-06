using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_wait tool using the test apps.
/// </summary>
[Collection("TestApps")]
public class WaitTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WaitTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Wait_Exists_FindsRunningWindow()
    {
        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var sw = Stopwatch.StartNew();
        var result = await _fixture.CallTool(tool, new
        {
            name = "FlaUI-MCP Test App",
            role = "window",
            state = "exists",
            timeout = 5
        });
        sw.Stop();

        _output.WriteLine($"Result: {result}");
        _output.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:F1}s");

        Assert.Contains("Found", result);
        Assert.Contains("FlaUI-MCP Test App", result);
        Assert.True(sw.Elapsed.TotalSeconds < 3, "Should find quickly");
    }

    [Fact]
    public async Task Wait_Gone_NonexistentWindow_ReturnsImmediately()
    {
        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var sw = Stopwatch.StartNew();
        var result = await _fixture.CallTool(tool, new
        {
            name = "This Window Does Not Exist 12345",
            role = "window",
            state = "gone",
            timeout = 5
        });
        sw.Stop();

        _output.WriteLine($"Result: {result}");
        _output.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:F1}s");

        Assert.Contains("gone", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 4, $"Should return before timeout but took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task Wait_Exists_WithHandle_FindsElement()
    {
        // Navigate to Buttons tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new
        {
            handle = _fixture.WinFormsHandle,
            name = "Click Me",
            role = "button",
            state = "exists",
            timeout = 5
        });
        _output.WriteLine($"Result: {result}");

        Assert.Contains("Found", result);
        Assert.Contains("Click Me", result);
        Assert.Contains("[ref=", result);
    }

    [Fact]
    public async Task Wait_Enabled_FindsEnabledButton()
    {
        // Navigate to Buttons tab, ensure checkbox is unchecked
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        // "Click Me" button is always enabled
        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new
        {
            handle = _fixture.WinFormsHandle,
            name = "Click Me",
            state = "enabled",
            timeout = 5
        });
        _output.WriteLine($"Result: {result}");

        Assert.Contains("Found", result);
    }

    [Fact]
    public async Task Wait_Timeout_ReturnsError()
    {
        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var sw = Stopwatch.StartNew();
        var result = await _fixture.CallTool(tool, new
        {
            name = "This Element Will Never Appear",
            state = "exists",
            timeout = 2
        });
        sw.Stop();

        _output.WriteLine($"Result: {result}");
        _output.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:F1}s");

        Assert.Contains("Timeout", result);
        Assert.True(sw.Elapsed.TotalSeconds >= 1.5, "Should wait near the timeout duration");
    }

    [Fact]
    public async Task Wait_NoCriteria_ReturnsError()
    {
        var tool = new WaitTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { state = "exists", timeout = 1 });
        _output.WriteLine($"Result: {result}");

        Assert.Contains("criterion", result, StringComparison.OrdinalIgnoreCase);
    }
}
