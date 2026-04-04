using System.Text.Json;
using FlaUI.Core.AutomationElements;
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
                description = "Click method: 'auto' (default, try Invoke/Toggle/Select then mouse), " +
                              "'invoke' (UIA Invoke pattern only), 'mouse' (physical mouse click only)"
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

            // Method: invoke — only use UIA patterns
            if (method == "invoke")
            {
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    return Task.FromResult(TextResult($"Invoked {elementName}"));
                }
                return Task.FromResult(ErrorResult($"Element {refId} does not support the Invoke pattern. Try method='mouse'."));
            }

            // Method: mouse — only use physical mouse click
            if (method == "mouse")
            {
                return Task.FromResult(PerformMouseClick(element, elementName, button, doubleClick));
            }

            // Method: auto — try patterns first, fall back to mouse
            if (button == "left" && !doubleClick)
            {
                // Try Invoke pattern (most reliable for buttons)
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    return Task.FromResult(TextResult($"Invoked {elementName}"));
                }

                // Try Toggle pattern for checkboxes
                if (element.Patterns.Toggle.IsSupported)
                {
                    element.Patterns.Toggle.Pattern.Toggle();
                    var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                    return Task.FromResult(TextResult($"Toggled {elementName} to {newState}"));
                }

                // Try SelectionItem pattern for list items
                if (element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return Task.FromResult(TextResult($"Selected {elementName}"));
                }
            }

            // Fall back to mouse click
            return Task.FromResult(PerformMouseClick(element, elementName, button, doubleClick));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to click {refId}: {ex.Message}"));
        }
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
