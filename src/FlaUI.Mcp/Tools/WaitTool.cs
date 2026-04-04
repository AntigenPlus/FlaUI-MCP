using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Wait for a UI element to appear, disappear, or reach a specific state
/// </summary>
public class WaitTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    public WaitTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_wait";

    public override string Description =>
        "Wait for a UI element to appear, disappear, or reach a specific state. " +
        "Polls the accessibility tree until the condition is met or timeout expires.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle to search within. If omitted, searches all windows via desktop."
            },
            name = new
            {
                type = "string",
                description = "Wait for element with this name (case-insensitive substring match)"
            },
            automationId = new
            {
                type = "string",
                description = "Wait for element with this AutomationId (exact match)"
            },
            role = new
            {
                type = "string",
                description = "Filter by role (e.g., \"button\", \"window\", \"textbox\")"
            },
            state = new
            {
                type = "string",
                description = "What condition to wait for (default: \"exists\")",
                @enum = new[] { "exists", "gone", "enabled", "focused" }
            },
            timeout = new
            {
                type = "integer",
                description = "Max wait time in seconds (default: 30)"
            }
        }
    };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var name = GetStringArgument(arguments, "name");
        var automationId = GetStringArgument(arguments, "automationId");
        var role = GetStringArgument(arguments, "role");
        var state = GetStringArgument(arguments, "state") ?? "exists";
        var timeout = GetArgument<int?>(arguments, "timeout") ?? 30;

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(automationId) && string.IsNullOrEmpty(role))
        {
            if (state != "gone" || string.IsNullOrEmpty(handle))
            {
                return ErrorResult("At least one search criterion (name, automationId, or role) is required.");
            }
        }

        ControlType? roleControlType = null;
        if (!string.IsNullOrEmpty(role))
        {
            roleControlType = MapRoleToControlType(role);
            if (roleControlType == null)
            {
                return ErrorResult($"Unknown role: {role}");
            }
        }

        var sw = Stopwatch.StartNew();
        var timeoutMs = timeout * 1000;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                AutomationElement? searchRoot = null;

                if (!string.IsNullOrEmpty(handle))
                {
                    var window = _sessionManager.GetWindow(handle);
                    if (window == null)
                    {
                        if (state == "gone")
                        {
                            var elapsed = sw.Elapsed.TotalSeconds;
                            return TextResult($"OK (element gone after {elapsed:F1}s)");
                        }
                        return ErrorResult($"Window not found: {handle}");
                    }
                    searchRoot = window;
                }
                else
                {
                    searchRoot = _sessionManager.Automation.GetDesktop();
                }

                // Search for matching elements
                AutomationElement? found = null;
                var hasCriteria = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(automationId) || roleControlType != null;

                if (hasCriteria)
                {
                    var descendants = searchRoot.FindAllDescendants();
                    foreach (var element in descendants)
                    {
                        if (MatchesElement(element, name, automationId, roleControlType))
                        {
                            found = element;
                            break;
                        }
                    }
                }

                switch (state)
                {
                    case "exists":
                        if (found != null)
                        {
                            var result = BuildFoundResult(found, handle, sw.Elapsed.TotalSeconds);
                            return result;
                        }
                        break;

                    case "gone":
                        if (!hasCriteria && !string.IsNullOrEmpty(handle))
                        {
                            // No criteria + handle: check if window still exists
                            // Window exists (we got past the null check above), keep waiting
                        }
                        else if (found == null)
                        {
                            var elapsed = sw.Elapsed.TotalSeconds;
                            return TextResult($"OK (element gone after {elapsed:F1}s)");
                        }
                        break;

                    case "enabled":
                        if (found != null && found.Properties.IsEnabled.ValueOrDefault)
                        {
                            var result = BuildFoundResult(found, handle, sw.Elapsed.TotalSeconds);
                            return result;
                        }
                        break;

                    case "focused":
                        if (found != null)
                        {
                            try
                            {
                                var focusedElement = _sessionManager.Automation.FocusedElement();
                                if (focusedElement != null &&
                                    found.Properties.AutomationId.ValueOrDefault == focusedElement.Properties.AutomationId.ValueOrDefault &&
                                    found.Properties.Name.ValueOrDefault == focusedElement.Properties.Name.ValueOrDefault &&
                                    found.Properties.ControlType.ValueOrDefault == focusedElement.Properties.ControlType.ValueOrDefault)
                                {
                                    var result = BuildFoundResult(found, handle, sw.Elapsed.TotalSeconds);
                                    return result;
                                }
                            }
                            catch
                            {
                                // Focus check can fail during transitions
                            }
                        }
                        break;
                }
            }
            catch
            {
                // UI elements can go stale or become inaccessible during transitions
                // For "gone" state, an exception accessing the element means it's gone
                if (state == "gone")
                {
                    var elapsed = sw.Elapsed.TotalSeconds;
                    return TextResult($"OK (element gone after {elapsed:F1}s)");
                }
            }

            await Task.Delay(250);
        }

        // Timeout
        var criteria = new List<string>();
        if (!string.IsNullOrEmpty(name)) criteria.Add($"name='{name}'");
        if (!string.IsNullOrEmpty(automationId)) criteria.Add($"automationId='{automationId}'");
        if (!string.IsNullOrEmpty(role)) criteria.Add($"role='{role}'");
        var criteriaStr = criteria.Count > 0 ? string.Join(", ", criteria) : "window";

        return ErrorResult($"Timeout after {timeout}s waiting for element ({criteriaStr}, state='{state}')");
    }

    private bool MatchesElement(AutomationElement element, string? name, string? automationId, ControlType? roleControlType)
    {
        if (!string.IsNullOrEmpty(name))
        {
            var elementName = element.Properties.Name.ValueOrDefault;
            if (string.IsNullOrEmpty(elementName) ||
                !elementName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(automationId))
        {
            var elementAutomationId = element.Properties.AutomationId.ValueOrDefault;
            if (!string.Equals(elementAutomationId, automationId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (roleControlType != null)
        {
            var elementControlType = element.Properties.ControlType.ValueOrDefault;
            if (elementControlType != roleControlType.Value)
            {
                return false;
            }
        }

        return true;
    }

    private McpToolResult BuildFoundResult(AutomationElement element, string? handle, double elapsedSeconds)
    {
        // Determine the window handle for registration
        var windowHandle = handle;
        if (string.IsNullOrEmpty(windowHandle))
        {
            // Walk up to find the containing window
            var current = element;
            Window? parentWindow = null;
            while (current != null)
            {
                if (current.Properties.ControlType.ValueOrDefault == ControlType.Window)
                {
                    parentWindow = current.AsWindow();
                    break;
                }
                current = current.Parent;
            }

            if (parentWindow != null)
            {
                windowHandle = _sessionManager.RegisterWindow(parentWindow);
            }
            else
            {
                // Fallback: register under a synthetic handle
                windowHandle = _sessionManager.RegisterWindow(element.AsWindow() ?? _sessionManager.Automation.GetDesktop().AsWindow()!);
            }
        }

        var refId = _elementRegistry.Register(windowHandle!, element);

        // Build the element description line
        var role = GetRoleString(element);
        var elementName = element.Properties.Name.ValueOrDefault;
        var states = new List<string>();

        if (!element.Properties.IsEnabled.ValueOrDefault)
            states.Add("disabled");
        if (element.Properties.IsOffscreen.ValueOrDefault)
            states.Add("offscreen");

        var line = role;
        if (!string.IsNullOrEmpty(elementName))
        {
            line += $" \"{elementName.Replace("\"", "\\\"")}\"";
        }
        line += $" [ref={refId}]";
        foreach (var s in states)
        {
            line += $" [{s}]";
        }

        return TextResult($"Found after {elapsedSeconds:F1}s\n{line}");
    }

    private static string GetRoleString(AutomationElement element)
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
                ControlType.Window => "window",
                ControlType.Group => "group",
                ControlType.Pane => "group",
                ControlType.DataGrid => "grid",
                ControlType.Hyperlink => "link",
                ControlType.Image => "image",
                ControlType.ToolBar => "toolbar",
                ControlType.Document => "document",
                _ => "element"
            };
        }
        catch
        {
            return "element";
        }
    }

    private static ControlType? MapRoleToControlType(string role) => role switch
    {
        "button" => ControlType.Button,
        "textbox" => ControlType.Edit,
        "text" => ControlType.Text,
        "checkbox" => ControlType.CheckBox,
        "radio" => ControlType.RadioButton,
        "combobox" => ControlType.ComboBox,
        "list" => ControlType.List,
        "listitem" => ControlType.ListItem,
        "menu" => ControlType.Menu,
        "menuitem" => ControlType.MenuItem,
        "menubar" => ControlType.MenuBar,
        "tree" => ControlType.Tree,
        "treeitem" => ControlType.TreeItem,
        "tablist" => ControlType.Tab,
        "tab" => ControlType.TabItem,
        "table" => ControlType.Table,
        "row" => ControlType.DataItem,
        "header" => ControlType.Header,
        "columnheader" => ControlType.HeaderItem,
        "slider" => ControlType.Slider,
        "window" => ControlType.Window,
        "group" => ControlType.Group,
        "grid" => ControlType.DataGrid,
        "link" => ControlType.Hyperlink,
        "image" => ControlType.Image,
        "toolbar" => ControlType.ToolBar,
        "document" => ControlType.Document,
        _ => null
    };
}
