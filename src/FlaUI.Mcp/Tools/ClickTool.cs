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

    /// <summary>
    /// How long to wait for UIA Invoke to complete before assuming the
    /// invoked handler is blocking (e.g. opened a modal) and returning a
    /// warning to the caller. The Invoke task continues in the background.
    /// </summary>
    private const int InvokeBlockingDetectionMs = 500;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return ErrorResult("Missing required argument: ref");
        }

        var button = GetStringArgument(arguments, "button") ?? "left";
        var doubleClick = GetBoolArgument(arguments, "doubleClick", false);

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs.");
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            // Try Invoke pattern first (most reliable for buttons).
            //
            // UIA Invoke is a synchronous COM call that holds an RPC channel
            // against the target process for the entire duration of the
            // invoked handler. If the handler doesn't return promptly — e.g.
            // because it called ShowDialog on a modal — the worker thread
            // would block forever and every subsequent UIA call into that
            // process would queue behind the held channel (#32).
            //
            // To avoid that, we dispatch the Invoke onto a background task
            // and race it against a short timeout. The common case (fast
            // handler) returns normally within a few ms. Slow/modal handlers
            // exceed the timeout — we return success with a WARNING that
            // explains the held-channel side-effect and points the caller at
            // the right FlaUI primitive (Mouse.Click(GetClickablePoint())).
            // The background Invoke is left running; it completes when the
            // invoked handler eventually returns.
            if (button == "left" && !doubleClick && element.Patterns.Invoke.IsSupported)
            {
                var invokeTask = Task.Run(() =>
                {
                    try
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // The tool call may have already returned with a
                        // warning, so we can't surface this to the caller.
                        // Log to stderr (MCP host log) instead of silently
                        // swallowing the error.
                        Console.Error.WriteLine($"Background Invoke failed for {elementName}: {ex.Message}");
                    }
                });

                var winner = await Task.WhenAny(invokeTask, Task.Delay(InvokeBlockingDetectionMs));
                if (winner == invokeTask)
                {
                    return TextResult($"Invoked {elementName}");
                }

                return TextResult(
                    $"Invoked {elementName}. WARNING: the invoked handler did not return within " +
                    $"{InvokeBlockingDetectionMs}ms — the target likely opened a modal dialog or is " +
                    $"doing slow synchronous work. UIA Invoke holds a per-process COM channel for " +
                    $"the duration of the call, so subsequent UIA tools (windows_snapshot, " +
                    $"windows_list_windows, windows_get_text, etc.) against this process will time " +
                    $"out until the handler returns. If you need to interact with the AUT while the " +
                    $"handler is running, dispatch this click via a physical mouse click instead — " +
                    $"in FlaUI that's Mouse.Click(element.GetClickablePoint()).");
            }

            // Try Toggle pattern for checkboxes
            if (button == "left" && !doubleClick && element.Patterns.Toggle.IsSupported)
            {
                element.Patterns.Toggle.Pattern.Toggle();
                var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return TextResult($"Toggled {elementName} to {newState}");
            }

            // Try SelectionItem pattern for list items
            if (button == "left" && !doubleClick && element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return TextResult($"Selected {elementName}");
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
                return TextResult($"Double-clicked {elementName}");
            }
            else
            {
                Mouse.Click(clickPoint, mouseButton);
                return TextResult($"Clicked {elementName}");
            }
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to click {refId}: {ex.Message}");
        }
    }
}
