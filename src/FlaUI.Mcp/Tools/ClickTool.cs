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
        "Click an element by its ref (from windows_snapshot). Prefers Invoke pattern for reliability, " +
        "falls back to mouse click if needed.";

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
            button = new
            {
                type = "string",
                @enum = new[] { "left", "right", "middle" },
                description = "Mouse button to click (default: left)"
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

            // Try Invoke pattern first (most reliable for buttons). Dispatch on a
            // background thread because Invoke() blocks until the invoked handler
            // returns, which never happens for buttons that open a modal via
            // ShowDialog — that would deadlock the single MCP worker thread (#32).
            if (button == "left" && !doubleClick && element.Patterns.Invoke.IsSupported)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // The tool call already returned, so we can't surface this
                        // to the caller. Log to stderr (MCP host log) so failures
                        // are at least observable instead of silently swallowed.
                        Console.Error.WriteLine($"Background Invoke failed for {elementName}: {ex.Message}");
                    }
                });
                return Task.FromResult(TextResult($"Invoked {elementName}"));
            }

            // Try Toggle pattern for checkboxes
            if (button == "left" && !doubleClick && element.Patterns.Toggle.IsSupported)
            {
                element.Patterns.Toggle.Pattern.Toggle();
                var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return Task.FromResult(TextResult($"Toggled {elementName} to {newState}"));
            }

            // Try SelectionItem pattern for list items
            if (button == "left" && !doubleClick && element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return Task.FromResult(TextResult($"Selected {elementName}"));
            }

            // Fall back to mouse click
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
                return Task.FromResult(TextResult($"Double-clicked {elementName}"));
            }
            else
            {
                Mouse.Click(clickPoint, mouseButton);
                return Task.FromResult(TextResult($"Clicked {elementName}"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to click {refId}: {ex.Message}"));
        }
    }
}
