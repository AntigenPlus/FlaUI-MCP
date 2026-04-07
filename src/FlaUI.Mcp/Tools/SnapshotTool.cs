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

    public SnapshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_snapshot";

    public override string Description =>
        "Capture accessibility snapshot of a window or element subtree. Returns a structured tree " +
        "with element refs that can be used with windows_click, windows_type, etc. This is the " +
        "primary tool for understanding window contents - use it before interacting with elements. " +
        "Use 'ref' to snapshot a specific element subtree (e.g., a modal dialog found via windows_find). " +
        "When translating snapshot output into FlaUI test code, prefer the 'id=' (AutomationId) " +
        "value over the quoted Name — Name can resolve differently across the process boundary " +
        "between this MCP server (out-of-process UIA) and an in-process test runner. AutomationId " +
        "is stable. See README \"Pitfalls When Translating MCP Output Into Test Code\".";

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
            },
            maxTableRows = new
            {
                type = "integer",
                description = "Maximum number of items to include when expanding table, grid, or list elements (default: 5). Headers are always included."
            },
            backend = new
            {
                type = "string",
                @enum = new[] { "uia3", "uia2" },
                description = "Automation backend to use. UIA3 (default) works best for WPF/UWP. UIA2 may provide better results for older WinForms controls."
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
        var backend = GetStringArgument(arguments, "backend");
        var maxTableRows = 5;
        if (arguments != null && arguments.Value.TryGetProperty("maxTableRows", out var maxRowsProp) && maxRowsProp.TryGetInt32(out var val))
        {
            maxTableRows = Math.Max(0, val);
        }
        var snapshotBuilder = new SnapshotBuilder(_elementRegistry, maxTableRows: maxTableRows);

        try
        {
            return Task.FromResult(UiaRetry.With(() =>
            {
                // If a ref is provided, snapshot that element's subtree
                if (!string.IsNullOrEmpty(refId))
                {
                    var element = _elementRegistry.GetElement(refId);
                    if (element == null)
                    {
                        return ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs.");
                    }

                    var windowHandle = ExtractWindowHandle(refId) ?? handle ?? "w0";
                    var snapshot = snapshotBuilder.BuildSnapshot(windowHandle, element);
                    return TextResult(snapshot);
                }

                // Otherwise snapshot the whole window
                FlaUI.Core.AutomationElements.Window? window = null;

                if (!string.IsNullOrEmpty(handle))
                {
                    window = _sessionManager.GetWindow(handle);
                    if (window == null)
                    {
                        return ErrorResult($"Window not found: {handle}");
                    }
                }
                else
                {
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
                        return ErrorResult("No window specified and no focused window found. Use windows_list_windows to see available windows.");
                    }

                    handle = _sessionManager.RegisterWindow(window);
                }

                // If UIA2 backend requested, get the window via its native handle
                if (string.Equals(backend, "uia2", StringComparison.OrdinalIgnoreCase))
                {
                    var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
                    if (hwnd != IntPtr.Zero)
                    {
                        var uia2Window = _sessionManager.UIA2Automation.FromHandle(hwnd)?.AsWindow();
                        if (uia2Window != null)
                        {
                            window = uia2Window;
                        }
                    }
                }

                var fullSnapshot = snapshotBuilder.BuildSnapshot(handle!, window);
                return TextResult(fullSnapshot);
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture snapshot: {ex.Message}"));
        }
    }

    private static string? ExtractWindowHandle(string refId)
    {
        var eIndex = refId.IndexOf('e');
        return eIndex > 0 ? refId[..eIndex] : null;
    }
}
