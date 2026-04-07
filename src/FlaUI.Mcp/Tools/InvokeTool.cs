using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Invoke an element's canonical UIA action — Invoke, Toggle, or SelectionItem
/// pattern, in priority order. This is a programmatic operation: it does not
/// move the cursor, fire mouse events, or require the window to be focused.
/// Use windows_click for a real mouse click.
///
/// Maps directly to FlaUI primitives:
/// - element.Patterns.Invoke.Pattern.Invoke()
/// - element.Patterns.Toggle.Pattern.Toggle()
/// - element.Patterns.SelectionItem.Pattern.Select()
/// </summary>
public class InvokeTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;

    public InvokeTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    /// <summary>
    /// How long to wait for UIA Invoke to complete before assuming the
    /// invoked handler is blocking (e.g. opened a modal) and returning a
    /// warning to the caller. The Invoke task continues in the background.
    /// </summary>
    private const int InvokeBlockingDetectionMs = 500;

    public override string Name => "windows_invoke";

    public override string Description =>
        "Trigger an element's canonical UIA action via the Invoke, Toggle, or SelectionItem " +
        "pattern (in that priority order). Programmatic — does not move the cursor, fire mouse " +
        "events, or require the target window to be focused. Maps to " +
        "element.Patterns.Invoke/Toggle/SelectionItem.Pattern.<action>() in FlaUI. " +
        "Use windows_click for a real mouse click (with modifiers, button choice, double-click).";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            @ref = new
            {
                type = "string",
                description = "Element ref from windows_snapshot (e.g., 'w1e5')"
            }
        },
        required = new[] { "ref" }
    };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return ErrorResult("Missing required argument: ref");
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs.");
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            if (element.Patterns.Invoke.IsSupported)
            {
                return await InvokeWithHangDetection(element, elementName);
            }

            if (element.Patterns.Toggle.IsSupported)
            {
                element.Patterns.Toggle.Pattern.Toggle();
                var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                return TextResult($"UIA Toggle: toggled {elementName} to {newState}");
            }

            if (element.Patterns.SelectionItem.IsSupported)
            {
                element.Patterns.SelectionItem.Pattern.Select();
                return TextResult($"UIA SelectionItem: selected {elementName}");
            }

            return ErrorResult(
                $"Element {refId} does not support any UIA invocation pattern (Invoke, Toggle, SelectionItem). " +
                $"Use windows_click for a physical mouse click.");
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to invoke {refId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Race UIA Invoke against a short timeout. UIA Invoke is a synchronous
    /// COM call that holds an RPC channel against the target process for the
    /// entire duration of the invoked handler. If the handler doesn't return
    /// promptly (e.g. it called ShowDialog on a modal), every subsequent UIA
    /// call into that process queues behind the held channel and times out
    /// (#32). When the timeout fires we return success with a warning so the
    /// caller knows what happened and which tool to switch to. The background
    /// Invoke is left running and completes when the invoked handler eventually
    /// returns.
    /// </summary>
    private static async Task<McpToolResult> InvokeWithHangDetection(AutomationElement element, string elementName)
    {
        var invokeTask = Task.Run(() =>
        {
            try
            {
                element.Patterns.Invoke.Pattern.Invoke();
            }
            catch (Exception ex)
            {
                // The tool call may have already returned with a warning,
                // so we can't surface this to the caller. Log to stderr
                // (MCP host log) instead of silently swallowing the error.
                Console.Error.WriteLine($"Background Invoke failed for {elementName}: {ex.Message}");
            }
        });

        var winner = await Task.WhenAny(invokeTask, Task.Delay(InvokeBlockingDetectionMs));
        if (winner == invokeTask)
        {
            return TextResult($"UIA Invoke: invoked {elementName}");
        }

        return TextResult(
            $"UIA Invoke: invoked {elementName}. WARNING: the invoked handler did not return within " +
            $"{InvokeBlockingDetectionMs}ms — the target likely opened a modal dialog or is doing " +
            $"slow synchronous work. UIA Invoke holds a per-process COM channel for the duration " +
            $"of the call, so subsequent UIA tools (windows_snapshot, windows_list_windows, " +
            $"windows_get_text, etc.) against this process will time out until the handler returns. " +
            $"If you need to interact with the AUT while the handler is running, call windows_click " +
            $"instead — it dispatches a physical mouse click via Mouse.Click(element.GetClickablePoint()) " +
            $"and holds no COM state.");
    }
}
