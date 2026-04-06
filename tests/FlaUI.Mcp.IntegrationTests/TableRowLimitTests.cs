using System.Diagnostics;
using System.Text.RegularExpressions;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for the maxTableRows snapshot parameter that limits DataGridView row expansion.
/// The WinForms test app has a 50-row DataGridView on the Grid tab.
/// </summary>
[Collection("TestApps")]
public class TableRowLimitTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TableRowLimitTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Navigate to the Grid tab and return a snapshot with the given maxTableRows.
    /// </summary>
    private async Task<string> GetGridSnapshot(int maxTableRows)
    {
        // Navigate to Grid tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Grid");
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        // Poll until grid content appears
        var sw = Stopwatch.StartNew();
        string gridSnapshot = "";
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            var builder = new SnapshotBuilder(_fixture.Elements, maxTableRows: maxTableRows);
            gridSnapshot = builder.BuildSnapshot(
                _fixture.WinFormsHandle, _fixture.GetWinFormsWindow()!);
            if (gridSnapshot.Contains("Test Data"))
                break;
        }

        Assert.Contains("Test Data", gridSnapshot);
        return gridSnapshot;
    }

    /// <summary>
    /// Count how many data row elements appear in a snapshot.
    /// Matches "element "Row N"" lines — not the "header "Row N"" children inside each row.
    /// </summary>
    private static int CountDataRows(string snapshot)
    {
        return Regex.Matches(snapshot, @"element ""Row \d+""").Count;
    }

    /// <summary>
    /// Extract the "... and N more rows" count, or null if not present.
    /// </summary>
    private static int? GetMoreRowsCount(string snapshot)
    {
        var match = Regex.Match(snapshot, @"\.\.\. and (\d+) more rows");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [Fact]
    public async Task DefaultLimit_ShowsOnly5Rows()
    {
        var snapshot = await GetGridSnapshot(5);
        _output.WriteLine(snapshot[..Math.Min(3000, snapshot.Length)]);

        var rowCount = CountDataRows(snapshot);
        var moreRows = GetMoreRowsCount(snapshot);

        _output.WriteLine($"Data rows shown: {rowCount}, more rows: {moreRows}");

        Assert.Equal(5, rowCount);
        Assert.NotNull(moreRows);
        Assert.True(moreRows > 0, "Should indicate more rows are available");
        // 50 total rows - 5 shown = 45 more
        Assert.Equal(45, moreRows);
    }

    [Fact]
    public async Task MaxTableRows_2_ShowsOnly2Rows()
    {
        var snapshot = await GetGridSnapshot(2);

        var rowCount = CountDataRows(snapshot);
        var moreRows = GetMoreRowsCount(snapshot);

        _output.WriteLine($"Data rows shown: {rowCount}, more rows: {moreRows}");

        Assert.Equal(2, rowCount);
        Assert.Equal(48, moreRows);
    }

    [Fact]
    public async Task MaxTableRows_0_ShowsNoDataRows()
    {
        var snapshot = await GetGridSnapshot(0);
        _output.WriteLine(snapshot[..Math.Min(2000, snapshot.Length)]);

        var rowCount = CountDataRows(snapshot);
        var moreRows = GetMoreRowsCount(snapshot);

        _output.WriteLine($"Data rows shown: {rowCount}, more rows: {moreRows}");

        Assert.Equal(0, rowCount);
        Assert.Equal(50, moreRows);
    }

    [Fact]
    public async Task HeadersAlwaysIncluded_RegardlessOfLimit()
    {
        var snapshot = await GetGridSnapshot(0);

        // Column headers should always be present even with 0 data rows
        Assert.Contains("Select", snapshot);
        Assert.Contains("ID", snapshot);
        Assert.Contains("Name", snapshot);
        Assert.Contains("Category", snapshot);
        Assert.Contains("Value", snapshot);
    }

    [Fact]
    public async Task HeaderContainer_NotCountedAsDataRow()
    {
        // This is the specific bug we found: the "Top Row" element containing
        // headers was being counted as a data row, consuming one of the maxTableRows slots.
        var snapshot = await GetGridSnapshot(1);
        _output.WriteLine(snapshot[..Math.Min(2000, snapshot.Length)]);

        // Should show exactly 1 data row (not 0 because "Top Row" ate the slot)
        var rowCount = CountDataRows(snapshot);
        _output.WriteLine($"Data rows shown: {rowCount}");
        Assert.Equal(1, rowCount);

        // Headers should still be present
        Assert.Contains("Select", snapshot);
        Assert.Contains("ID", snapshot);
    }

    [Fact]
    public async Task LargeLimit_ShowsAllRows()
    {
        var snapshot = await GetGridSnapshot(100);

        var rowCount = CountDataRows(snapshot);
        var moreRows = GetMoreRowsCount(snapshot);

        _output.WriteLine($"Data rows shown: {rowCount}, more rows: {moreRows}");

        // All 50 rows should be shown, no truncation
        Assert.Equal(50, rowCount);
        Assert.Null(moreRows); // No "more rows" line
    }

    [Fact]
    public async Task NonTableElements_NotAffected()
    {
        // Elements outside the table should not be affected by maxTableRows
        var snapshot = await GetGridSnapshot(2);

        // The status panel below the grid should still be present
        Assert.Contains("Select All", snapshot);
        Assert.Contains("Clear Selection", snapshot);
        Assert.Contains("items selected", snapshot);
    }
}
