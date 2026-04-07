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

            // For elements that support Invoke (typically buttons), prefer
            // Mouse.Click over UIA Invoke. UIA Invoke is a synchronous COM call
            // that holds an RPC channel against the target process for the entire
            // duration of the invoked handler. When the handler is slow (e.g. a
            // button that calls ShowDialog on a form whose Load handler queries
            // a database), every subsequent UIA call into that process serializes
            // behind the held channel and times out (issue #32). Mouse.Click is
            // dispatched via Win32 SendInput, which doesn't hold any COM state.
            //
            // Falls back to fire-and-forget Invoke when no clickable point is
            // available (e.g. offscreen elements). The fire-and-forget path
            // unblocks the MCP worker thread but does NOT prevent the held-COM
            // hang — agents calling this path should expect subsequent UIA
            // requests against the same process to be delayed until the invoked
            // handler returns.
            if (button == "left" && !doubleClick && element.Patterns.Invoke.IsSupported)
            {
                if (TryGetClickablePoint(element, out var invokeClickPoint))
                {
                    // Bring the element's containing window to the foreground so
                    // Mouse.Click hits it instead of whatever is currently on top.
                    // Mouse.Click sends to absolute screen coordinates and respects
                    // Z-order — UIA Invoke didn't, which is part of why we used it.
                    BringToForeground(element);
                    Mouse.Click(invokeClickPoint, MouseButton.Left);
                    return Task.FromResult(TextResult($"Clicked {elementName}"));
                }

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
                return Task.FromResult(TextResult($"Invoked {elementName} (no clickable point — UIA Invoke fallback)"));
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

    private static bool TryGetClickablePoint(FlaUI.Core.AutomationElements.AutomationElement element, out System.Drawing.Point point)
    {
        try
        {
            point = element.GetClickablePoint();
            return true;
        }
        catch
        {
            point = default;
            return false;
        }
    }

    private static void BringToForeground(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        try
        {
            var current = element;
            while (current != null
                && current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window)
            {
                current = current.Parent;
            }
            current?.AsWindow()?.SetForeground();
        }
        catch
        {
            // Best-effort: if we can't walk parents or set foreground, fall
            // through to the click anyway. The click may go to the wrong window
            // but failing here would prevent legitimate clicks on focused windows.
        }
    }
}
