using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Read a table or grid control and return its data as a structured markdown table
/// </summary>
public class TableTool : ToolBase
{
    private const int DefaultMaxRows = 100;

    private readonly ElementRegistry _elementRegistry;

    public TableTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_table";

    public override string Description =>
        "Read a table or grid control and return its data as a structured markdown table. " +
        "Use on table/grid elements found in windows_snapshot.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            @ref = new
            {
                type = "string",
                description = "Element ref of a table/grid control from windows_snapshot (e.g., 'w1e5')"
            },
            rows = new
            {
                type = "string",
                description = "Row range like '0-9', '0', '5-14'. Default: first 100 rows."
            },
            columns = new
            {
                type = "string",
                description = "Comma-separated column names to include. Default: all columns."
            }
        },
        required = new[] { "ref" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult("Missing required argument: ref"));
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        try
        {
            var controlType = element.Properties.ControlType.ValueOrDefault;
            if (controlType != ControlType.Table && controlType != ControlType.DataGrid)
            {
                return Task.FromResult(ErrorResult(
                    $"Element {refId} is not a table or grid control (found: {controlType}). " +
                    "Use this tool on Table or DataGrid elements from windows_snapshot."));
            }

            var rowsParam = GetStringArgument(arguments, "rows");
            var columnsParam = GetStringArgument(arguments, "columns");

            // Fetch children once and reuse across header detection and row reading
            var allChildren = element.FindAllChildren();

            // Detect data rows: standard DataItem or WinForms "Row N" pattern
            var dataRows = FindDataRows(allChildren);
            int totalRows = dataRows.Length;

            // Try Grid pattern for standard DataItem rows
            if (dataRows.Length > 0
                && dataRows[0].Properties.ControlType.ValueOrDefault == ControlType.DataItem
                && element.Patterns.Grid.IsSupported)
            {
                try
                {
                    return Task.FromResult(ReadViaGridPattern(
                        element, allChildren, totalRows, rowsParam, columnsParam));
                }
                catch
                {
                    // Grid pattern may throw (e.g., ElementNotAvailableException); fall through
                }
            }

            // Tree walking handles both standard and non-standard row types
            return Task.FromResult(ReadViaTreeWalking(
                allChildren, dataRows, rowsParam, columnsParam));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to read table {refId}: {ex.Message}"));
        }
    }

    private static AutomationElement[] FindDataRows(AutomationElement[] allChildren)
    {
        return allChildren
            .Where(c =>
            {
                try
                {
                    var ct = c.Properties.ControlType.ValueOrDefault;
                    if (ct == ControlType.DataItem) return true;
                    if (ct == ControlType.Header || ct == ControlType.ScrollBar) return false;
                    // WinForms DataGridView uses Custom type with "Row N" names
                    var name = c.Properties.Name.ValueOrDefault ?? "";
                    return Regex.IsMatch(name, @"^Row \d+$");
                }
                catch { return false; }
            })
            .ToArray();
    }

    private McpToolResult ReadViaGridPattern(
        AutomationElement element, AutomationElement[] allChildren, int totalRows,
        string? rowsParam, string? columnsParam)
    {
        var gridPattern = element.Patterns.Grid.Pattern;
        int totalCols = gridPattern.ColumnCount;
        var headers = GetColumnHeaders(allChildren, totalCols);
        var (columnIndices, columnNames) = FilterColumns(headers, columnsParam, totalCols);
        var (startRow, endRow) = ParseRowRange(rowsParam, totalRows);

        // Build cell accessor for the grid pattern
        string GetCell(int row, int col)
        {
            var cell = gridPattern.GetItem(row, col);
            return cell != null ? GetCellValue(cell) : "";
        }

        return BuildMarkdownTable(columnNames, columnIndices, totalRows, startRow, endRow, GetCell);
    }

    private McpToolResult ReadViaTreeWalking(
        AutomationElement[] allChildren, AutomationElement[] dataRows,
        string? rowsParam, string? columnsParam)
    {
        int totalRows = dataRows.Length;

        // Determine column count from first data row (excluding row headers)
        int totalCols = 0;
        AutomationElement[]? firstRowDataCells = null;
        if (dataRows.Length > 0)
        {
            var firstRowChildren = dataRows[0].FindAllChildren();
            firstRowDataCells = FilterDataCells(firstRowChildren);
            totalCols = firstRowDataCells.Length;
        }

        var headers = GetColumnHeaders(allChildren, totalCols);
        var (columnIndices, columnNames) = FilterColumns(headers, columnsParam, totalCols);
        var (startRow, endRow) = ParseRowRange(rowsParam, totalRows);

        // Cache cell arrays per row to avoid redundant FindAllChildren calls
        // (first row already fetched above)
        var rowCellCache = new Dictionary<int, AutomationElement[]>();
        if (firstRowDataCells != null) rowCellCache[0] = firstRowDataCells;

        string GetCell(int row, int col)
        {
            if (!rowCellCache.TryGetValue(row, out var cells))
            {
                cells = FilterDataCells(dataRows[row].FindAllChildren());
                rowCellCache[row] = cells;
            }
            return col < cells.Length ? GetCellValue(cells[col]) : "";
        }

        return BuildMarkdownTable(columnNames, columnIndices, totalRows, startRow, endRow, GetCell);
    }

    /// <summary>
    /// Filter out row header elements from a row's children, returning only data cells.
    /// </summary>
    private static AutomationElement[] FilterDataCells(AutomationElement[] rowChildren)
    {
        return rowChildren.Where(c =>
        {
            try { return c.Properties.ControlType.ValueOrDefault != ControlType.Header; }
            catch { return true; }
        }).ToArray();
    }

    /// <summary>
    /// Shared markdown table builder used by both grid pattern and tree walking paths.
    /// </summary>
    private McpToolResult BuildMarkdownTable(
        List<string> columnNames, List<int> columnIndices,
        int totalRows, int startRow, int endRow,
        Func<int, int, string> getCell)
    {
        var sb = new StringBuilder();

        // Header row
        sb.Append('|');
        foreach (var name in columnNames)
            sb.Append($" {EscapePipe(name)} |");
        sb.AppendLine();

        // Separator row
        sb.Append('|');
        foreach (var _ in columnNames)
            sb.Append(" --- |");
        sb.AppendLine();

        // Data rows
        for (int row = startRow; row <= endRow && row < totalRows; row++)
        {
            sb.Append('|');
            foreach (var col in columnIndices)
                sb.Append($" {EscapePipe(getCell(row, col))} |");
            sb.AppendLine();
        }

        int shownEnd = Math.Min(endRow, totalRows - 1);
        sb.AppendLine($"({totalRows} total rows, showing {startRow}-{shownEnd})");

        return TextResult(sb.ToString());
    }

    // --- Header detection ---

    private List<string> GetColumnHeaders(AutomationElement[] allChildren, int totalCols)
    {
        List<string> headers;

        // Strategy 1: Header container with HeaderItem children (standard WPF/Win32)
        headers = TryHeaderContainerWithHeaderItems(allChildren);
        if (HasMeaningfulNames(headers))
            return PadHeaders(headers, totalCols);

        // Strategy 2: Header row container with Header children (WinForms DataGridView)
        headers = TryHeaderRowContainer(allChildren);
        if (HasMeaningfulNames(headers))
            return PadHeaders(headers, totalCols);

        // Strategy 3: Cell names from first DataItem row (last resort — names may be values)
        headers = TryCellNamesFromFirstRow(allChildren);
        if (HasMeaningfulNames(headers))
            return PadHeaders(headers, totalCols);

        // Strategy 4: Generic Column0, Column1, etc.
        return PadHeaders(new List<string>(), totalCols);
    }

    private static List<string> TryHeaderContainerWithHeaderItems(AutomationElement[] allChildren)
    {
        var headers = new List<string>();
        foreach (var child in allChildren)
        {
            try
            {
                if (child.Properties.ControlType.ValueOrDefault != ControlType.Header)
                    continue;
                var headerItems = child.FindAllChildren(c => c.ByControlType(ControlType.HeaderItem));
                foreach (var item in headerItems)
                    headers.Add(item.Properties.Name.ValueOrDefault ?? "");
                if (headers.Count > 0) return headers;
            }
            catch { }
        }
        return headers;
    }

    private static List<string> TryHeaderRowContainer(AutomationElement[] allChildren)
    {
        // Find a child whose children are mostly Headers (the column header row).
        // The first such child before any data rows is the header container.
        var headers = new List<string>();
        foreach (var child in allChildren)
        {
            try
            {
                var ct = child.Properties.ControlType.ValueOrDefault;
                if (ct == ControlType.ScrollBar) continue;

                // Stop searching once we hit data rows
                var name = child.Properties.Name.ValueOrDefault ?? "";
                if (ct == ControlType.DataItem || Regex.IsMatch(name, @"^Row \d+$"))
                    break;

                var grandchildren = child.FindAllChildren();
                if (grandchildren.Length < 2) continue;

                var headerChildren = grandchildren
                    .Where(gc =>
                    {
                        try { return gc.Properties.ControlType.ValueOrDefault == ControlType.Header; }
                        catch { return false; }
                    })
                    .ToArray();

                if (headerChildren.Length >= 2 && headerChildren.Length >= grandchildren.Length / 2)
                {
                    // Skip the corner cell: first header with an empty name or generic name
                    // that doesn't match subsequent header naming patterns
                    bool skippedCorner = false;
                    foreach (var h in headerChildren)
                    {
                        var hName = h.Properties.Name.ValueOrDefault ?? "";
                        if (!skippedCorner && headers.Count == 0 && headerChildren.Length > 2)
                        {
                            // If the first header name looks like a corner cell (empty, or
                            // doesn't match the pattern of subsequent headers), skip it
                            var nextName = headerChildren.Length > 1
                                ? headerChildren[1].Properties.Name.ValueOrDefault ?? ""
                                : "";
                            if (string.IsNullOrEmpty(hName) || hName.Length > nextName.Length * 3)
                            {
                                skippedCorner = true;
                                continue;
                            }
                        }
                        headers.Add(hName);
                    }
                    if (headers.Count > 0) return headers;
                }
            }
            catch { }
        }
        return headers;
    }

    private static List<string> TryCellNamesFromFirstRow(AutomationElement[] allChildren)
    {
        var headers = new List<string>();
        foreach (var child in allChildren)
        {
            try
            {
                if (child.Properties.ControlType.ValueOrDefault == ControlType.DataItem)
                {
                    var cells = child.FindAllChildren();
                    foreach (var cell in cells)
                        headers.Add(cell.Properties.Name.ValueOrDefault ?? "");
                    return headers;
                }
            }
            catch { }
        }
        return headers;
    }

    // --- Helpers ---

    private static bool HasMeaningfulNames(List<string> headers)
    {
        return headers.Count > 0 && headers.Any(h => !string.IsNullOrEmpty(h));
    }

    private static List<string> PadHeaders(List<string> headers, int totalCols)
    {
        while (headers.Count < totalCols)
            headers.Add($"Column{headers.Count}");
        return headers;
    }

    private static (List<int> indices, List<string> names) FilterColumns(
        List<string> allHeaders, string? columnsParam, int totalCols)
    {
        if (string.IsNullOrEmpty(columnsParam))
        {
            return (Enumerable.Range(0, totalCols).ToList(), allHeaders.Take(totalCols).ToList());
        }

        var requestedNames = columnsParam.Split(',').Select(s => s.Trim()).ToList();
        var indices = new List<int>();
        var names = new List<string>();

        foreach (var requested in requestedNames)
        {
            for (int i = 0; i < allHeaders.Count; i++)
            {
                if (string.Equals(allHeaders[i], requested, StringComparison.OrdinalIgnoreCase))
                {
                    indices.Add(i);
                    names.Add(allHeaders[i]);
                    break;
                }
            }
        }

        return (indices, names);
    }

    private static (int start, int end) ParseRowRange(string? rowsParam, int totalRows)
    {
        if (string.IsNullOrEmpty(rowsParam))
        {
            return (0, Math.Min(totalRows - 1, DefaultMaxRows - 1));
        }

        var parts = rowsParam.Split('-');
        if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out int single))
        {
            return (single, single);
        }
        if (parts.Length == 2
            && int.TryParse(parts[0].Trim(), out int start)
            && int.TryParse(parts[1].Trim(), out int end))
        {
            return (start, end);
        }

        return (0, Math.Min(totalRows - 1, DefaultMaxRows - 1));
    }

    private static string GetCellValue(AutomationElement? cell)
    {
        if (cell == null) return "";
        try
        {
            if (cell.Patterns.Value.IsSupported)
            {
                var val = cell.Patterns.Value.Pattern.Value.ValueOrDefault;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            if (cell.Patterns.Toggle.IsSupported)
            {
                var state = cell.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return state == ToggleState.On ? "[x]" : "[ ]";
            }
            return cell.Properties.Name.ValueOrDefault ?? "";
        }
        catch { return ""; }
    }

    private static string EscapePipe(string value)
    {
        return value.Replace("|", "\\|");
    }
}
