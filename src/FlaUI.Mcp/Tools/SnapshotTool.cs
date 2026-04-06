using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Take accessibility snapshot of a window or element subtree - THE KEY TOOL FOR AGENTS
/// </summary>
public class SnapshotTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly SnapshotBuilder _snapshotBuilder;

    public SnapshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _snapshotBuilder = new SnapshotBuilder(elementRegistry);
    }

    public override string Name => "windows_snapshot";

    public override string Description =>
        "Capture accessibility snapshot of a window or element subtree. Returns a structured tree " +
        "with element refs that can be used with windows_click, windows_type, etc. This is the " +
        "primary tool for understanding window contents - use it before interacting with elements. " +
        "Use 'ref' to snapshot a specific element subtree (e.g., a modal dialog found via windows_find).";

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
            @ref = new
            {
                type = "string",
                description = "Element ref to snapshot a subtree instead of the whole window. " +
                              "Useful for inspecting modal dialogs or specific panels without capturing the entire window."
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");

        try
        {
            // If a ref is provided, snapshot that element's subtree
            if (!string.IsNullOrEmpty(refId))
            {
                var element = _elementRegistry.GetElement(refId);
                if (element == null)
                {
                    return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
                }

                // Derive the window handle from the ref (e.g., "w2e15" → "w2")
                var windowHandle = ExtractWindowHandle(refId);
                if (string.IsNullOrEmpty(windowHandle))
                {
                    windowHandle = handle ?? "w0";
                }

                var snapshot = _snapshotBuilder.BuildSnapshot(windowHandle, element);
                return Task.FromResult(TextResult(snapshot));
            }

            // Otherwise snapshot the whole window
            FlaUI.Core.AutomationElements.Window? window = null;

            if (!string.IsNullOrEmpty(handle))
            {
                window = _sessionManager.GetWindow(handle);
                if (window == null)
                {
                    return Task.FromResult(ErrorResult($"Window not found: {handle}"));
                }
            }
            else
            {
                // Get the foreground window
                var desktop = _sessionManager.Automation.GetDesktop();
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

            var fullSnapshot = _snapshotBuilder.BuildSnapshot(handle!, window);
            return Task.FromResult(TextResult(fullSnapshot));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture snapshot: {ex.Message}"));
        }
    }

    /// <summary>
    /// Extract the window handle prefix from a ref string (e.g., "w2e15" → "w2").
    /// </summary>
    private static string? ExtractWindowHandle(string refId)
    {
        var eIndex = refId.IndexOf('e');
        return eIndex > 0 ? refId[..eIndex] : null;
    }
}
