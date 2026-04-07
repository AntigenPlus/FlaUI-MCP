using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Diagnostic tool that dumps the AutomationId of every descendant element
/// of a window or subtree, formatted as a compact table. AutomationId is the
/// stable identifier test code should target — Name is derived from many
/// sources and can differ between out-of-process UIA (what the MCP sees) and
/// in-process UIA (what test code at runtime sees). Use this when the
/// regular windows_snapshot is too noisy and you just want a focused list
/// of identifiable controls.
/// </summary>
public class DumpIdsTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    public DumpIdsTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_dump_ids";

    public override string Description =>
        "Dump every descendant element with an AutomationId, formatted as a compact table " +
        "(AutomationId | ControlType | Name | Rect). Use this to discover the stable " +
        "identifiers test code should target. Pass 'handle' for a window, 'ref' for a " +
        "subtree, 'filter' to narrow by AutomationId regex, or 'includeEmptyIds=true' to " +
        "include controls without an AutomationId.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle from windows_launch or windows_list_windows."
            },
            @ref = new
            {
                type = "string",
                description = "Element ref to dump a subtree instead of a whole window."
            },
            filter = new
            {
                type = "string",
                description = "Optional regex to filter results by AutomationId (case-insensitive)."
            },
            includeEmptyIds = new
            {
                type = "boolean",
                description = "Include controls that have no AutomationId (default: false)."
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
        var filterStr = GetStringArgument(arguments, "filter");
        var includeEmptyIds = GetBoolArgument(arguments, "includeEmptyIds", false);

        Regex? filter = null;
        if (!string.IsNullOrEmpty(filterStr))
        {
            try
            {
                filter = new Regex(filterStr, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(ErrorResult($"Invalid filter regex: {ex.Message}"));
            }
        }

        AutomationElement? root = null;

        try
        {
            if (!string.IsNullOrEmpty(refId))
            {
                root = _elementRegistry.GetElement(refId);
                if (root == null)
                {
                    return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
                }
            }
            else if (!string.IsNullOrEmpty(handle))
            {
                root = _sessionManager.GetWindow(handle);
                if (root == null)
                {
                    return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                }
            }
            else
            {
                return Task.FromResult(ErrorResult("Specify 'handle' or 'ref' to choose what to dump."));
            }

            return Task.FromResult(UiaRetry.With(() =>
            {
                var rows = new List<Row>();
                Walk(root, rows, includeEmptyIds, filter);

                if (rows.Count == 0)
                {
                    var what = filter != null ? $"matching /{filterStr}/" : "with AutomationId";
                    return TextResult($"No descendants {what} found.");
                }

                return TextResult(FormatTable(rows));
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to dump ids: {ex.Message}"));
        }
    }

    private static void Walk(AutomationElement element, List<Row> rows, bool includeEmptyIds, Regex? filter)
    {
        try
        {
            var id = SnapshotBuilder.TryGetAutomationId(element);
            var include = includeEmptyIds || !string.IsNullOrEmpty(id);
            if (include && (filter == null || (id != null && filter.IsMatch(id))))
            {
                rows.Add(new Row(
                    Id: id ?? "",
                    ControlType: SnapshotBuilder.GetElementRole(element),
                    Name: SafeName(element),
                    Rect: SafeRect(element)
                ));
            }

            foreach (var child in element.FindAllChildren())
            {
                Walk(child, rows, includeEmptyIds, filter);
            }
        }
        catch
        {
            // Some elements throw on property access — skip them.
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string SafeRect(AutomationElement element)
    {
        try
        {
            var r = element.Properties.BoundingRectangle.ValueOrDefault;
            return $"{(int)r.X},{(int)r.Y},{(int)r.Width},{(int)r.Height}";
        }
        catch
        {
            return "";
        }
    }

    private static string FormatTable(List<Row> rows)
    {
        // Pad columns to a width that fits the widest entry, capped so a runaway
        // long Name doesn't make every line wide.
        int idW = Math.Min(40, Math.Max(12, rows.Max(r => r.Id.Length)));
        int typeW = Math.Min(15, Math.Max(11, rows.Max(r => r.ControlType.Length)));
        int nameW = Math.Min(40, Math.Max(4, rows.Max(r => r.Name.Length)));

        var sb = new StringBuilder();
        sb.AppendLine($"{"AutomationId".PadRight(idW)} | {"ControlType".PadRight(typeW)} | {"Name".PadRight(nameW)} | Rect");
        sb.AppendLine($"{new string('-', idW)}-+-{new string('-', typeW)}-+-{new string('-', nameW)}-+-{new string('-', 18)}");
        foreach (var r in rows)
        {
            sb.AppendLine($"{Truncate(r.Id, idW).PadRight(idW)} | {Truncate(r.ControlType, typeW).PadRight(typeW)} | {Truncate(r.Name, nameW).PadRight(nameW)} | {r.Rect}");
        }
        sb.AppendLine();
        sb.AppendLine($"{rows.Count} element(s)");
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private record Row(string Id, string ControlType, string Name, string Rect);
}
