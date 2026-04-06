using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Search for elements matching criteria, returning only matching elements instead of the full tree
/// </summary>
public class FindTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    public FindTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_find";

    public override string Description =>
        "Search for UI elements matching criteria. Returns matching elements with refs that can be used " +
        "with other tools. More targeted than windows_snapshot - use when you know what you're looking for.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle from windows_launch or windows_list_windows. If omitted, uses the focused window."
            },
            name = new
            {
                type = "string",
                description = "Substring match on element Name property (case-insensitive)"
            },
            automationId = new
            {
                type = "string",
                description = "Exact match on AutomationId"
            },
            role = new
            {
                type = "string",
                description = "Filter by role (e.g., \"button\", \"textbox\", \"table\", \"checkbox\")"
            },
            depth = new
            {
                type = "integer",
                description = "Max traversal depth (default: 10)"
            },
            pattern = new
            {
                type = "string",
                description = "Filter by supported UIA pattern: \"invoke\", \"value\", \"toggle\", \"selection\", \"expandcollapse\", \"scroll\", \"grid\", \"table\", \"text\", \"rangevalue\"",
                @enum = new[] { "invoke", "value", "toggle", "selection", "expandcollapse", "scroll", "grid", "table", "text", "rangevalue" }
            },
            maxResults = new
            {
                type = "integer",
                description = "Maximum number of results to return (default: 50)"
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var nameFilter = GetStringArgument(arguments, "name");
        var automationIdFilter = GetStringArgument(arguments, "automationId");
        var roleFilter = GetStringArgument(arguments, "role");
        var depthArg = GetArgument<int?>(arguments, "depth");
        var patternFilter = GetStringArgument(arguments, "pattern");
        var maxResultsArg = GetArgument<int?>(arguments, "maxResults");

        int maxDepth = depthArg ?? 10;
        int maxResults = maxResultsArg ?? 50;

        try
        {
            FlaUI.Core.AutomationElements.Window? window = null;

            if (!string.IsNullOrEmpty(handle))
            {
                window = _sessionManager.GetWindow(handle);
                if (window == null)
                {
                    return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                }

                // Get a fresh UIA element via the native window handle to avoid
                // stale cached properties from a previous snapshot
                try
                {
                    var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
                    if (hwnd != IntPtr.Zero)
                    {
                        var freshElement = _sessionManager.Automation.FromHandle(hwnd);
                        var freshWindow = freshElement?.AsWindow();
                        if (freshWindow != null)
                        {
                            window = freshWindow;
                        }
                    }
                }
                catch
                {
                    // Fall through to use the cached window if refresh fails
                }
            }
            else
            {
                // Get the foreground window (same logic as SnapshotTool)
                var focusedElement = _sessionManager.Automation.FocusedElement();

                if (focusedElement != null)
                {
                    var current = focusedElement;
                    while (current != null)
                    {
                        if (current.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Window)
                        {
                            window = current.AsWindow();
                            break;
                        }
                        current = current.Parent;
                    }
                }

                if (window == null)
                {
                    return Task.FromResult(ErrorResult("No window specified and no focused window found. Use windows_list_windows to see available windows."));
                }

                handle = _sessionManager.RegisterWindow(window);
            }

            // Validate pattern filter if provided
            if (!string.IsNullOrEmpty(patternFilter) && !IsValidPattern(patternFilter))
            {
                return Task.FromResult(ErrorResult($"Unknown pattern: {patternFilter}. Valid patterns: invoke, value, toggle, selection, expandcollapse, scroll, grid, table, text, rangevalue"));
            }

            var matches = new List<(AutomationElement Element, string RefId)>();
            FindMatchingElements(handle!, window, matches, nameFilter, automationIdFilter, roleFilter, patternFilter, 0, maxDepth, maxResults);

            if (matches.Count == 0)
            {
                return Task.FromResult(TextResult("No matching elements found."));
            }

            var sb = new StringBuilder();
            var truncated = matches.Count >= maxResults;
            sb.AppendLine(truncated
                ? $"Found {matches.Count}+ matching element(s) (limit {maxResults}):"
                : $"Found {matches.Count} matching element(s):");
            foreach (var (element, refId) in matches)
            {
                var line = SnapshotBuilder.FormatElementLine(element, refId);
                sb.AppendLine($"- {line}");
            }

            return Task.FromResult(TextResult(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Find failed: {ex.Message}"));
        }
    }

    private void FindMatchingElements(
        string windowHandle,
        AutomationElement element,
        List<(AutomationElement, string)> matches,
        string? nameFilter,
        string? automationIdFilter,
        string? roleFilter,
        string? patternFilter,
        int currentDepth,
        int maxDepth,
        int maxResults)
    {
        if (currentDepth > maxDepth) return;
        if (matches.Count >= maxResults) return;

        if (MatchesCriteria(element, nameFilter, automationIdFilter, roleFilter, patternFilter))
        {
            var refId = _elementRegistry.Register(windowHandle, element);
            matches.Add((element, refId));
            if (matches.Count >= maxResults) return;
        }

        // Recurse into children
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                FindMatchingElements(windowHandle, child, matches, nameFilter, automationIdFilter, roleFilter, patternFilter, currentDepth + 1, maxDepth, maxResults);
                if (matches.Count >= maxResults) return;
            }
        }
        catch
        {
            // Some elements throw when accessing children
        }
    }

    private static bool MatchesCriteria(
        AutomationElement element,
        string? nameFilter,
        string? automationIdFilter,
        string? roleFilter,
        string? patternFilter)
    {
        // At least one filter must be specified (otherwise everything matches)
        bool hasFilter = !string.IsNullOrEmpty(nameFilter)
                      || !string.IsNullOrEmpty(automationIdFilter)
                      || !string.IsNullOrEmpty(roleFilter)
                      || !string.IsNullOrEmpty(patternFilter);
        if (!hasFilter) return false;

        // Name: substring match, case-insensitive
        if (!string.IsNullOrEmpty(nameFilter))
        {
            try
            {
                var elementName = element.Properties.Name.ValueOrDefault;
                if (string.IsNullOrEmpty(elementName) ||
                    !elementName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // AutomationId: exact match
        if (!string.IsNullOrEmpty(automationIdFilter))
        {
            try
            {
                var autoId = element.Properties.AutomationId.ValueOrDefault;
                if (!string.Equals(autoId, automationIdFilter, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // Role: match against the mapped role string
        if (!string.IsNullOrEmpty(roleFilter))
        {
            var role = SnapshotBuilder.GetElementRole(element);
            if (!string.Equals(role, roleFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Pattern: check if the element supports the specified UIA pattern
        if (!string.IsNullOrEmpty(patternFilter))
        {
            if (!SupportsPattern(element, patternFilter))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidPattern(string pattern)
    {
        return pattern.ToLowerInvariant() switch
        {
            "invoke" or "value" or "toggle" or "selection" or
            "expandcollapse" or "scroll" or "grid" or "table" or
            "text" or "rangevalue" => true,
            _ => false
        };
    }

    private static bool SupportsPattern(AutomationElement element, string pattern)
    {
        try
        {
            return pattern.ToLowerInvariant() switch
            {
                "invoke" => element.Patterns.Invoke.IsSupported,
                "value" => element.Patterns.Value.IsSupported,
                "toggle" => element.Patterns.Toggle.IsSupported,
                "selection" => element.Patterns.SelectionItem.IsSupported,
                "expandcollapse" => element.Patterns.ExpandCollapse.IsSupported,
                "scroll" => element.Patterns.Scroll.IsSupported,
                "grid" => element.Patterns.Grid.IsSupported,
                "table" => element.Patterns.Table.IsSupported,
                "text" => element.Patterns.Text.IsSupported,
                "rangevalue" => element.Patterns.RangeValue.IsSupported,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
}
