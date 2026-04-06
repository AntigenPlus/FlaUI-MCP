using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Take accessibility snapshot of a window - THE KEY TOOL FOR AGENTS
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
        "Capture accessibility snapshot of a window. Returns a structured tree with element refs " +
        "that can be used with windows_click, windows_type, etc. This is the primary tool for " +
        "understanding window contents - use it before interacting with elements.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle from windows_launch or windows_list_windows. If omitted, uses the most recently launched window."
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
        var backend = GetStringArgument(arguments, "backend");
        var maxTableRows = 5;
        if (arguments != null && arguments.Value.TryGetProperty("maxTableRows", out var maxRowsProp) && maxRowsProp.TryGetInt32(out var val))
        {
            maxTableRows = Math.Max(0, val);
        }
        var snapshotBuilder = new SnapshotBuilder(_elementRegistry, maxTableRows: maxTableRows);

        try
        {
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
                    // Walk up to find the window
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

                // Register this window
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

            var snapshot = snapshotBuilder.BuildSnapshot(handle!, window);
            return Task.FromResult(TextResult(snapshot));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture snapshot: {ex.Message}"));
        }
    }
}
