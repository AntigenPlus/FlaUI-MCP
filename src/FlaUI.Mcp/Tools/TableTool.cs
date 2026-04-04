using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Read a table or grid control and return its data as a structured markdown table
/// </summary>
public class TableTool : ToolBase
{
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
                description = "Row range like '0-9', '0', '5-14'. Default: all rows."
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

            // Try Grid pattern first
            if (element.Patterns.Grid.IsSupported)
            {
                return Task.FromResult(ReadViaGridPattern(element, rowsParam, columnsParam));
            }

            // Fall back to tree walking
            return Task.FromResult(ReadViaTreeWalking(element, rowsParam, columnsParam));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to read table {refId}: {ex.Message}"));
        }
    }

    private McpToolResult ReadViaGridPattern(AutomationElement element, string? rowsParam, string? columnsParam)
    {
        var gridPattern = element.Patterns.Grid.Pattern;
        int totalRows = gridPattern.RowCount;
        int totalCols = gridPattern.ColumnCount;

        // Read headers from Header children
        var headers = GetColumnHeaders(element, totalCols);

        // Determine which columns to include
        var (columnIndices, columnNames) = FilterColumns(headers, columnsParam, totalCols);

        // Parse row range
        var (startRow, endRow) = ParseRowRange(rowsParam, totalRows);

        // Build markdown table
        var sb = new StringBuilder();

        // Header row
        sb.Append('|');
        foreach (var name in columnNames)
        {
            sb.Append($" {EscapePipe(name)} |");
        }
        sb.AppendLine();

        // Separator row
        sb.Append('|');
        foreach (var _ in columnNames)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // Data rows
        for (int row = startRow; row <= endRow && row < totalRows; row++)
        {
            sb.Append('|');
            foreach (var col in columnIndices)
            {
                var cell = gridPattern.GetItem(row, col);
                var value = GetCellValue(cell);
                sb.Append($" {EscapePipe(value)} |");
            }
            sb.AppendLine();
        }

        int shownStart = startRow;
        int shownEnd = Math.Min(endRow, totalRows - 1);
        sb.AppendLine($"({totalRows} total rows, showing {shownStart}-{shownEnd})");

        return TextResult(sb.ToString());
    }

    private McpToolResult ReadViaTreeWalking(AutomationElement element, string? rowsParam, string? columnsParam)
    {
        // Find header items
        var headerElements = element.FindAllChildren(c =>
            c.ByControlType(ControlType.Header));
        var headers = new List<string>();
        if (headerElements.Length > 0)
        {
            var headerItems = headerElements[0].FindAllChildren(c =>
                c.ByControlType(ControlType.HeaderItem));
            foreach (var item in headerItems)
            {
                headers.Add(item.Properties.Name.ValueOrDefault ?? "");
            }
        }

        // Find data rows
        var dataItems = element.FindAllChildren(c =>
            c.ByControlType(ControlType.DataItem));
        int totalRows = dataItems.Length;

        // If no headers found, generate default names
        int totalCols = headers.Count;
        if (totalCols == 0 && dataItems.Length > 0)
        {
            var firstRowCells = dataItems[0].FindAllChildren();
            totalCols = firstRowCells.Length;
            for (int i = 0; i < totalCols; i++)
            {
                headers.Add($"Column{i}");
            }
        }

        // Determine which columns to include
        var (columnIndices, columnNames) = FilterColumns(headers, columnsParam, totalCols);

        // Parse row range
        var (startRow, endRow) = ParseRowRange(rowsParam, totalRows);

        // Build markdown table
        var sb = new StringBuilder();

        // Header row
        sb.Append('|');
        foreach (var name in columnNames)
        {
            sb.Append($" {EscapePipe(name)} |");
        }
        sb.AppendLine();

        // Separator row
        sb.Append('|');
        foreach (var _ in columnNames)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // Data rows
        for (int row = startRow; row <= endRow && row < totalRows; row++)
        {
            var cells = dataItems[row].FindAllChildren();
            sb.Append('|');
            foreach (var col in columnIndices)
            {
                var value = col < cells.Length ? GetCellValue(cells[col]) : "";
                sb.Append($" {EscapePipe(value)} |");
            }
            sb.AppendLine();
        }

        int shownStart = startRow;
        int shownEnd = Math.Min(endRow, totalRows - 1);
        sb.AppendLine($"({totalRows} total rows, showing {shownStart}-{shownEnd})");

        return TextResult(sb.ToString());
    }

    private List<string> GetColumnHeaders(AutomationElement element, int totalCols)
    {
        var headers = new List<string>();
        var headerElements = element.FindAllChildren(c =>
            c.ByControlType(ControlType.Header));
        if (headerElements.Length > 0)
        {
            var headerItems = headerElements[0].FindAllChildren(c =>
                c.ByControlType(ControlType.HeaderItem));
            foreach (var item in headerItems)
            {
                headers.Add(item.Properties.Name.ValueOrDefault ?? "");
            }
        }

        // Pad with default names if needed
        while (headers.Count < totalCols)
        {
            headers.Add($"Column{headers.Count}");
        }

        return headers;
    }

    private (List<int> indices, List<string> names) FilterColumns(List<string> allHeaders, string? columnsParam, int totalCols)
    {
        if (string.IsNullOrEmpty(columnsParam))
        {
            var allIndices = Enumerable.Range(0, totalCols).ToList();
            return (allIndices, allHeaders.Take(totalCols).ToList());
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

    private (int start, int end) ParseRowRange(string? rowsParam, int totalRows)
    {
        if (string.IsNullOrEmpty(rowsParam))
        {
            return (0, totalRows - 1);
        }

        var parts = rowsParam.Split('-');
        if (parts.Length == 1)
        {
            if (int.TryParse(parts[0].Trim(), out int single))
            {
                return (single, single);
            }
        }
        else if (parts.Length == 2)
        {
            if (int.TryParse(parts[0].Trim(), out int start) &&
                int.TryParse(parts[1].Trim(), out int end))
            {
                return (start, end);
            }
        }

        return (0, totalRows - 1);
    }

    private string GetCellValue(AutomationElement cell)
    {
        try
        {
            // Try Value pattern
            if (cell.Patterns.Value.IsSupported)
            {
                var val = cell.Patterns.Value.Pattern.Value.ValueOrDefault;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            // Try Toggle pattern for checkbox cells
            if (cell.Patterns.Toggle.IsSupported)
            {
                var state = cell.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return state == ToggleState.On ? "[x]" : "[ ]";
            }
            // Fall back to Name
            return cell.Properties.Name.ValueOrDefault ?? "";
        }
        catch { return ""; }
    }

    private static string EscapePipe(string value)
    {
        return value.Replace("|", "\\|");
    }
}
