using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Dispatch a physical mouse click to an element. Real input via Win32
/// SendInput: cursor moves, mouse events fire, modifier keys are honored,
/// the target window is brought to the foreground first.
///
/// Maps directly to FlaUI: Mouse.Click(element.GetClickablePoint(), button).
///
/// Use windows_invoke for the programmatic UIA Invoke/Toggle/Select action
/// when you don't want a real mouse event (e.g. clicking offscreen elements
/// or avoiding focus changes).
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
        "Dispatch a physical mouse click to an element via Win32 SendInput. The cursor moves, " +
        "mouse events fire, and the target window is brought to the foreground first. Supports " +
        "modifier keys (Ctrl, Shift, Alt, Win), button choice (left/right/middle), and " +
        "double-click. Maps to Mouse.Click(element.GetClickablePoint(), button) in FlaUI. " +
        "Use windows_invoke for the programmatic UIA action without moving the cursor.";

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
            },
            modifiers = new
            {
                type = "array",
                items = new { type = "string", @enum = new[] { "ctrl", "shift", "alt", "win" } },
                description = "Modifier keys to hold while clicking (e.g., [\"ctrl\"] for Ctrl-click " +
                              "multi-select). Pressed before the click and released after."
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
        var modifierNames = GetArgument<string[]>(arguments, "modifiers") ?? Array.Empty<string>();

        if (!ModifierKeyParser.TryParse(modifierNames, out var modifierKeys, out var modifierError))
        {
            return Task.FromResult(ErrorResult(modifierError!));
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        try
        {
            var elementName = element.Properties.Name.ValueOrDefault ?? refId;

            // WinForms DataGridView checkboxes need a mouse double-click to fire
            // CellContentClick + EndEdit (so the underlying data model updates).
            // A single click only enters edit mode. We auto-promote single-click
            // to double-click in this specific case. Still mouse input — just a
            // count tweak — so the choice is honest about what it's doing.
            if (button == "left" && !doubleClick && modifierKeys.Count == 0 && IsGridCheckbox(element))
            {
                return Task.FromResult(PerformMouseClick(element, elementName, "left", doubleClick: true, modifierKeys, modifierNames));
            }

            return Task.FromResult(PerformMouseClick(element, elementName, button, doubleClick, modifierKeys, modifierNames));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to click {refId}: {ex.Message}"));
        }
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

    private static McpToolResult PerformMouseClick(
        AutomationElement element,
        string elementName,
        string button,
        bool doubleClick,
        IReadOnlyList<VirtualKeyShort> modifierKeys,
        IReadOnlyList<string> modifierNames)
    {
        // Mouse.Click sends to absolute screen coordinates and respects Z-order:
        // if the target window is occluded the click hits whatever is on top.
        // Bring the element's containing top-level window to the foreground so
        // the click reaches the right window. Mirrors what a FlaUI test author
        // would do (window.Focus() / SetForegroundWindow before click).
        BringToForeground(element);

        var clickPoint = element.GetClickablePoint();

        var mouseButton = button switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        // Hold modifiers around the click. Press in forward order, release in
        // reverse order, mirroring how a real keyboard handles modifier stacks.
        for (int i = 0; i < modifierKeys.Count; i++) Keyboard.Press(modifierKeys[i]);
        try
        {
            if (doubleClick)
            {
                Mouse.DoubleClick(clickPoint, mouseButton);
            }
            else
            {
                Mouse.Click(clickPoint, mouseButton);
            }
        }
        finally
        {
            for (int i = modifierKeys.Count - 1; i >= 0; i--) Keyboard.Release(modifierKeys[i]);
        }

        var verb = doubleClick ? "Double-clicked" : "Clicked";
        if (modifierNames.Count > 0)
        {
            var modList = string.Join("+", modifierNames);
            return TextResult($"{verb} {elementName} with [{modList}] held");
        }
        return TextResult($"{verb} {elementName}");
    }

    /// <summary>
    /// Walk up to the element's containing top-level window and bring it to
    /// the foreground. Best-effort — failures are silent so a click on a
    /// focused element doesn't fail when foreground walking can't find a parent.
    /// </summary>
    private static void BringToForeground(AutomationElement element)
    {
        try
        {
            var current = element;
            while (current != null
                && current.Properties.ControlType.ValueOrDefault != ControlType.Window)
            {
                current = current.Parent;
            }
            current?.AsWindow()?.SetForeground();
        }
        catch
        {
        }
    }

}
