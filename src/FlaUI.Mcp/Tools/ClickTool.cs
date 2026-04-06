using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Click an element by ref
/// </summary>
public class ClickTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;

    public ClickTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_click";

    public override string Description =>
        "Click an element by its ref (from windows_snapshot). By default, prefers the Invoke " +
        "pattern for reliability and falls back to mouse click. Use method='mouse' to force a " +
        "physical mouse click, which is needed for controls that respond to mouse events rather " +
        "than the UIA Invoke pattern (e.g., custom grid cells with popup menus).";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            @ref = new
            {
                type = "string",
                description = "Element ref from windows_snapshot (e.g., 'w1e5')"
            },
            method = new
            {
                type = "string",
                @enum = new[] { "auto", "invoke", "mouse" },
                description = "Click method: 'auto' (default, try UIA patterns then mouse), " +
                              "'invoke' (UIA patterns only — Invoke, Toggle, Select), 'mouse' (physical mouse click only)"
            },
            button = new
            {
                type = "string",
                @enum = new[] { "left", "right", "middle" },
                description = "Mouse button to click (default: left). Only used for mouse clicks."
            },
            doubleClick = new
            {
                type = "boolean",
                description = "Whether to double-click (default: false)"
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

        var method = GetStringArgument(arguments, "method") ?? "auto";
        var button = GetStringArgument(arguments, "button") ?? "left";
        var doubleClick = GetBoolArgument(arguments, "doubleClick", false);

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            if (method == "invoke")
            {
                if (button != "left" || doubleClick)
                {
                    return Task.FromResult(ErrorResult(
                        "The 'invoke' method only supports single left clicks. Use method='mouse' for right-click or double-click."));
                }

                var result = TryUiaPatterns(element, elementName);
                if (result != null) return Task.FromResult(result);
                return Task.FromResult(ErrorResult(
                    $"Element {refId} does not support UIA click patterns (Invoke, Toggle, Select). Try method='mouse'."));
            }

            if (method == "mouse")
            {
                return Task.FromResult(PerformMouseClick(element, elementName, button, doubleClick));
            }

            // Method: auto — for DataGridView checkboxes, use two mouse clicks
            // because UIA Invoke toggles the visual state but doesn't fire
            // CellContentClick/EndEdit, so the data model isn't updated.
            // The first mouse click enters edit mode, the second toggles the value.
            if (button == "left" && !doubleClick && IsGridCheckbox(element))
            {
                var clickPoint = element.GetClickablePoint();
                Mouse.Click(clickPoint, MouseButton.Left);
                Thread.Sleep(50);
                Mouse.Click(clickPoint, MouseButton.Left);
                return Task.FromResult(TextResult($"Clicked {elementName} (grid checkbox)"));
            }

            // Method: auto — try UIA patterns first (for simple left clicks), fall back to mouse
            if (button == "left" && !doubleClick)
            {
                var result = TryUiaPatterns(element, elementName);
                if (result != null) return Task.FromResult(result);
            }

            return Task.FromResult(PerformMouseClick(element, elementName, button, doubleClick));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to click {refId}: {ex.Message}"));
        }
    }

    /// <summary>
    /// Try UIA patterns in priority order: Invoke, Toggle, SelectionItem.
    /// Returns null if no pattern is supported.
    /// </summary>
    private static McpToolResult? TryUiaPatterns(AutomationElement element, string elementName)
    {
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return TextResult($"Invoked {elementName}");
        }

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
            return TextResult($"Toggled {elementName} to {newState}");
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return TextResult($"Selected {elementName}");
        }

        return null;
    }

    /// <summary>
    /// Check if an element is a checkbox/toggle inside a DataGridView (table/grid).
    /// These require a mouse double-click to toggle because:
    /// - UIA Invoke doesn't fire CellContentClick or EndEdit
    /// - A single mouse click only enters edit mode without toggling
    /// </summary>
    private static bool IsGridCheckbox(AutomationElement element)
    {
        try
        {
            var ct = element.Properties.ControlType.ValueOrDefault;
            if (ct != ControlType.CheckBox && !element.Patterns.Toggle.IsSupported)
                return false;

            // Walk up to check if an ancestor is a table or grid
            var parent = element.Parent;
            for (int i = 0; i < 3 && parent != null; i++)
            {
                var parentType = parent.Properties.ControlType.ValueOrDefault;
                if (parentType == ControlType.Table || parentType == ControlType.DataGrid)
                    return true;
                parent = parent.Parent;
            }
        }
        catch { }
        return false;
    }

    private static McpToolResult PerformMouseClick(AutomationElement element, string elementName, string button, bool doubleClick)
    {
        var clickPoint = element.GetClickablePoint();

        var mouseButton = button switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        if (doubleClick)
        {
            Mouse.DoubleClick(clickPoint, mouseButton);
            return TextResult($"Double-clicked {elementName}");
        }
        else
        {
            Mouse.Click(clickPoint, mouseButton);
            return TextResult($"Clicked {elementName}");
        }
    }
}
