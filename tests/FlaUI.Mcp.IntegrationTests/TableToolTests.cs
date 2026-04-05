using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_table tool using the WinForms test app's 50-row DataGridView.
/// </summary>
[Collection("TestApps")]
public class TableToolTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TableToolTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Navigate to Grid tab and return the table element ref.
    /// </summary>
    private async Task<string> GetGridRef()
    {
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Grid");
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        // Poll for grid to appear
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            var gridRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Test Data");
            if (gridRef != null) return gridRef;
        }

        Assert.Fail("Test Data grid not found after navigating to Grid tab.");
        return "";
    }

    [Fact]
    public async Task Table_ReturnsMarkdownFormat()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0-2" });
        _output.WriteLine(result);

        // Should have markdown table structure
        Assert.Contains("|", result);
        Assert.Contains("---", result);
        Assert.Contains("total rows", result);
    }

    [Fact]
    public async Task Table_HasCorrectColumnHeaders()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0" });
        _output.WriteLine(result);

        // Should use real column names, not Column0, Column1
        Assert.Contains("Select", result);
        Assert.Contains("ID", result);
        Assert.Contains("Name", result);
        Assert.Contains("Category", result);
        Assert.Contains("Value", result);
        Assert.DoesNotContain("Column0", result);
    }

    [Fact]
    public async Task Table_RowRangeFilter()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0-4" });
        _output.WriteLine(result);

        // Should show rows 0-4 (5 rows) out of 50
        Assert.Contains("showing 0-4", result);
        Assert.Contains("50 total rows", result);
    }

    [Fact]
    public async Task Table_SingleRow()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0" });
        _output.WriteLine(result);

        Assert.Contains("showing 0-0", result);
    }

    [Fact]
    public async Task Table_ColumnFilter()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0-2", columns = "ID,Name" });
        _output.WriteLine(result);

        // Should include ID and Name columns
        Assert.Contains("ID", result);
        Assert.Contains("Name", result);
        // Should not include other columns (except in data which might coincidentally match)
        // Check the header line specifically
        var headerLine = result.Split('\n').First(l => l.Contains("ID") && l.Contains("|"));
        Assert.DoesNotContain("Category", headerLine);
    }

    [Fact]
    public async Task Table_NonTableElement_ReturnsError()
    {
        // Find a button ref and try to use it as a table
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var buttonRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(buttonRef);

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine(result);

        Assert.Contains("not a table", result);
    }

    [Fact]
    public async Task Table_ContainsDeterministicData()
    {
        var gridRef = await GetGridRef();

        var tool = new TableTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = gridRef, rows = "0" });
        _output.WriteLine(result);

        // First row should have ITEM-001 (deterministic data from seeded random)
        Assert.Contains("ITEM-001", result);
        Assert.Contains("Test Item 1", result);
        Assert.Contains("Alpha", result); // First category
    }
}
