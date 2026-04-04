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
    private readonly SnapshotBuilder _snapshotBuilder;

    public SnapshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _snapshotBuilder = new SnapshotBuilder(elementRegistry);
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

            // If UIA2 backend requested, re-find the window using UIA2
            if (string.Equals(backend, "uia2", StringComparison.OrdinalIgnoreCase))
            {
                var uia2 = _sessionManager.UIA2Automation;
                var uia2Desktop = uia2.GetDesktop();
                var windowTitle = window.Title;
                var uia2Window = uia2Desktop.FindFirstDescendant(cf => cf.ByName(windowTitle))?.AsWindow();
                if (uia2Window != null)
                {
                    window = uia2Window;
                }
            }

            var snapshot = _snapshotBuilder.BuildSnapshot(handle!, window);
            return Task.FromResult(TextResult(snapshot));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture snapshot: {ex.Message}"));
        }
    }
}
