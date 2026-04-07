using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Builds agent-friendly accessibility snapshots from UI Automation trees
/// </summary>
public class SnapshotBuilder
{
    private readonly ElementRegistry _elementRegistry;
    private readonly int _maxDepth;
    private readonly int _maxTableRows;

    public SnapshotBuilder(ElementRegistry elementRegistry, int maxDepth = 10, int maxTableRows = 5)
    {
        _elementRegistry = elementRegistry;
        _maxDepth = maxDepth;
        _maxTableRows = maxTableRows;
    }

    public string BuildSnapshot(string windowHandle, AutomationElement root)
    {
        // Clear previous elements for this window
        _elementRegistry.ClearWindow(windowHandle);

        var sb = new StringBuilder();
        BuildElementSnapshot(sb, windowHandle, root, 0);
        return sb.ToString();
    }

    private void BuildElementSnapshot(StringBuilder sb, string windowHandle, AutomationElement element, int depth)
    {
        if (depth > _maxDepth) return;

        // Skip elements with no meaningful content
        var name = GetElementName(element);
        var role = GetElementRole(element);
        
        // Skip some noise elements, but keep elements with names or important roles
        if (ShouldSkipElement(element, name, role)) return;

        // Register element and get ref
        var refId = _elementRegistry.Register(windowHandle, element);

        // Build the line
        var indent = new string(' ', depth * 2);
        var line = BuildElementLine(element, refId, name, role);
        sb.AppendLine($"{indent}- {line}");

        // Process children
        try
        {
            var children = element.FindAllChildren();

            // For table/grid/list elements, limit the number of data rows/items shown
            if (role is "table" or "grid" or "list")
            {
                var itemCount = 0;
                var totalItems = 0;
                var foundDataRow = false;

                foreach (var child in children)
                {
                    var childRole = GetElementRole(child);

                    // Always include headers and structural elements (scrollbars, etc.)
                    if (childRole is "header" or "columnheader" or "scrollbar" or "thumb")
                    {
                        BuildElementSnapshot(sb, windowHandle, child, depth + 1);
                        continue;
                    }

                    // Before any data rows are found, check if this element is a header
                    // container (e.g., DataGridView "Top Row" with column header children).
                    // Skip this check once data rows are found to avoid expensive
                    // FindAllChildren calls on every row.
                    if (!foundDataRow && IsHeaderContainer(child))
                    {
                        BuildElementSnapshot(sb, windowHandle, child, depth + 1);
                        continue;
                    }

                    // Everything else is a data item (row, listitem, or element with row-like content)
                    foundDataRow = true;
                    totalItems++;
                    if (itemCount < _maxTableRows)
                    {
                        BuildElementSnapshot(sb, windowHandle, child, depth + 1);
                        itemCount++;
                    }
                }

                if (totalItems > _maxTableRows)
                {
                    var remaining = totalItems - _maxTableRows;
                    var childIndent = new string(' ', (depth + 1) * 2);
                    sb.AppendLine($"{childIndent}... and {remaining} more {(role == "list" ? "items" : "rows")}");
                }
            }
            else
            {
                foreach (var child in children)
                {
                    BuildElementSnapshot(sb, windowHandle, child, depth + 1);
                }
            }
        }
        catch
        {
            // Some elements throw when accessing children
        }
    }

    public static string FormatElementLine(AutomationElement element, string refId)
    {
        var name = GetElementName(element);
        var role = GetElementRole(element);
        return BuildElementLine(element, refId, name, role);
    }

    private static string BuildElementLine(AutomationElement element, string refId, string? name, string role)
    {
        var parts = new List<string>();

        // Role first
        parts.Add(role);

        // Name in quotes if present
        if (!string.IsNullOrEmpty(name))
        {
            parts.Add($"\"{EscapeName(name)}\"");
        }

        // Ref + AutomationId. AutomationId is the stable identifier test code
        // should target — Name is derived from many sources and can differ
        // between out-of-process UIA (what the MCP sees) and in-process UIA
        // (what test code at runtime sees). Surfacing both lets the agent
        // make an informed choice about which to put in their FlaUI test (#36).
        var automationId = TryGetAutomationId(element);
        parts.Add(string.IsNullOrEmpty(automationId)
            ? $"[ref={refId}]"
            : $"[ref={refId}, id={automationId}]");

        // State indicators
        var states = GetStateIndicators(element);
        if (states.Count > 0)
        {
            parts.AddRange(states.Select(s => $"[{s}]"));
        }

        return string.Join(" ", parts);
    }

    public static string? TryGetAutomationId(AutomationElement element)
    {
        try
        {
            var id = element.Properties.AutomationId.ValueOrDefault;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    public static string GetElementRole(AutomationElement element)
    {
        try
        {
            var controlType = element.Properties.ControlType.ValueOrDefault;
            return controlType switch
            {
                ControlType.Button => "button",
                ControlType.Edit => "textbox",
                ControlType.Text => "text",
                ControlType.CheckBox => "checkbox",
                ControlType.RadioButton => "radio",
                ControlType.ComboBox => "combobox",
                ControlType.List => "list",
                ControlType.ListItem => "listitem",
                ControlType.Menu => "menu",
                ControlType.MenuItem => "menuitem",
                ControlType.MenuBar => "menubar",
                ControlType.Tree => "tree",
                ControlType.TreeItem => "treeitem",
                ControlType.Tab => "tablist",
                ControlType.TabItem => "tab",
                ControlType.Table => "table",
                ControlType.DataItem => "row",
                ControlType.Header => "header",
                ControlType.HeaderItem => "columnheader",
                ControlType.Slider => "slider",
                ControlType.Spinner => "spinbutton",
                ControlType.ProgressBar => "progressbar",
                ControlType.Hyperlink => "link",
                ControlType.Image => "image",
                ControlType.Pane => "group",
                ControlType.Group => "group",
                ControlType.Window => "window",
                ControlType.Document => "document",
                ControlType.ToolBar => "toolbar",
                ControlType.ToolTip => "tooltip",
                ControlType.ScrollBar => "scrollbar",
                ControlType.StatusBar => "status",
                ControlType.Separator => "separator",
                ControlType.Thumb => "thumb",
                ControlType.TitleBar => "titlebar",
                ControlType.DataGrid => "grid",
                ControlType.Custom => "custom",
                _ => "element"
            };
        }
        catch
        {
            return "element";
        }
    }

    public static string? GetElementName(AutomationElement element)
    {
        try
        {
            var name = element.Properties.Name.ValueOrDefault;
            return string.IsNullOrWhiteSpace(name) ? null : name;
            // Note: previously this fell back to [automationId] when Name was
            // empty. The id is now surfaced separately in the ref annotation,
            // so the fallback would just duplicate it.
        }
        catch
        {
            return null;
        }
    }

    public static List<string> GetStateIndicators(AutomationElement element)
    {
        var states = new List<string>();

        try
        {
            if (!element.Properties.IsEnabled.ValueOrDefault)
                states.Add("disabled");

            if (element.Properties.IsOffscreen.ValueOrDefault)
                states.Add("offscreen");

            // Check for readonly (ValuePattern)
            if (element.Patterns.Value.IsSupported)
            {
                var valuePattern = element.Patterns.Value.Pattern;
                if (valuePattern.IsReadOnly.ValueOrDefault)
                    states.Add("readonly");
            }

            // Check toggle state
            if (element.Patterns.Toggle.IsSupported)
            {
                var toggleState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                if (toggleState == ToggleState.On)
                    states.Add("checked");
                else if (toggleState == ToggleState.Indeterminate)
                    states.Add("indeterminate");
            }

            // Check selection state
            if (element.Patterns.SelectionItem.IsSupported)
            {
                if (element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault)
                    states.Add("selected");
            }

            // Check expanded state
            if (element.Patterns.ExpandCollapse.IsSupported)
            {
                var expandState = element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault;
                if (expandState == ExpandCollapseState.Expanded)
                    states.Add("expanded");
                else if (expandState == ExpandCollapseState.Collapsed)
                    states.Add("collapsed");
            }
        }
        catch
        {
            // Ignore state query errors
        }

        return states;
    }

    private bool ShouldSkipElement(AutomationElement element, string? name, string role)
    {
        // Always include named elements
        if (!string.IsNullOrEmpty(name)) return false;

        // Always include elements with an AutomationId — they're explicitly
        // named in source code, which means the agent likely cares about them
        // (test-target candidates).
        if (!string.IsNullOrEmpty(TryGetAutomationId(element))) return false;

        // Always include actionable element types
        if (role is "button" or "textbox" or "checkbox" or "radio" or "combobox" 
            or "listitem" or "menuitem" or "tab" or "treeitem" or "link" or "slider")
        {
            return false;
        }

        // Include structural elements that might contain others
        if (role is "window" or "group" or "list" or "tree" or "tablist" 
            or "menu" or "menubar" or "toolbar" or "grid" or "table")
        {
            return false;
        }

        // Skip decorative/structural elements without names
        if (role is "element" or "thumb" or "scrollbar" or "separator" or "titlebar")
        {
            return true;
        }

        return false;
    }

    private static bool IsHeaderContainer(AutomationElement element)
    {
        try
        {
            var children = element.FindAllChildren();
            if (children.Length == 0) return false;
            foreach (var child in children)
            {
                var childType = child.Properties.ControlType.ValueOrDefault;
                if (childType != ControlType.Header && childType != ControlType.HeaderItem)
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string EscapeName(string name)
    {
        return name
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
