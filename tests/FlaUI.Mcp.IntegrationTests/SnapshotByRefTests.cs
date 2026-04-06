using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_snapshot with ref parameter for subtree snapshots.
/// </summary>
[Collection("TestApps")]
public class SnapshotByRefTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SnapshotByRefTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Snapshot_ByRef_ReturnsSubtreeOnly()
    {
        // Navigate to Buttons tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        // Find the Radio Group ref
        var groupRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Radio Group");
        Assert.NotNull(groupRef);
        _output.WriteLine($"Radio Group ref: {groupRef}");

        // Snapshot just the Radio Group subtree
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = groupRef });
        _output.WriteLine($"Subtree snapshot:\n{result}");

        // Should contain the radio buttons
        Assert.Contains("Option A", result);
        Assert.Contains("Option B", result);
        Assert.Contains("Option C", result);

        // Should NOT contain elements outside the Radio Group
        Assert.DoesNotContain("Click Me", result);
        Assert.DoesNotContain("Conditional Button", result);
    }

    [Fact]
    public async Task Snapshot_ByRef_IsMuchSmallerThanFullWindow()
    {
        // Full window snapshot
        var fullSnapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        _output.WriteLine($"Full snapshot length: {fullSnapshot.Length}");

        // Find a small subtree
        var tabRef = TestAppFixture.FindRefInSnapshot(fullSnapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var groupRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Radio Group");
        Assert.NotNull(groupRef);

        // Subtree snapshot
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var subtreeResult = await _fixture.CallTool(tool, new { @ref = groupRef });
        _output.WriteLine($"Subtree snapshot length: {subtreeResult.Length}");

        Assert.True(subtreeResult.Length < fullSnapshot.Length / 2,
            $"Subtree ({subtreeResult.Length}) should be much smaller than full window ({fullSnapshot.Length})");
    }

    [Fact]
    public async Task Snapshot_ByRef_InvalidRef_ReturnsError()
    {
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = "w999e999" });
        _output.WriteLine($"Result: {result}");

        Assert.Contains("Element not found", result);
    }

    [Fact]
    public async Task Snapshot_ByRef_RefsAreUsable()
    {
        // Navigate to Buttons tab and get a subtree snapshot
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var groupRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Radio Group");
        Assert.NotNull(groupRef);

        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var subtreeResult = await _fixture.CallTool(tool, new { @ref = groupRef });

        // Extract a ref from the subtree snapshot
        var radioRef = TestAppFixture.FindRefInSnapshot(subtreeResult, "Option B");
        Assert.NotNull(radioRef);
        _output.WriteLine($"Option B ref from subtree: {radioRef}");

        // The ref should be usable with other tools
        var clickResult = await _fixture.CallTool(clickTool, new { @ref = radioRef });
        _output.WriteLine($"Click result: {clickResult}");
        Assert.DoesNotContain("not found", clickResult);
    }
}
