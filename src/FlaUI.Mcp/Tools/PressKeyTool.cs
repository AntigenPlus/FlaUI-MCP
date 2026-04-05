using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Press a key or key combination. Sends keyboard input to the focused element.
/// </summary>
public class PressKeyTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;

    public PressKeyTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_press_key";

    public override string Description =>
        "Press a key or key combination. Sends keyboard input to the currently focused element. " +
        "Use this for individual key presses (Enter, Escape, arrow keys, single characters as key events) " +
        "rather than text input. Use windows_type for typing text strings.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            key = new
            {
                type = "string",
                description = "Key to press: 'a'-'z', '0'-'9', 'enter', 'escape', 'tab', 'space', " +
                              "'backspace', 'delete', 'up', 'down', 'left', 'right', 'home', 'end', " +
                              "'pageup', 'pagedown', 'f1'-'f12', or symbols like '+', '-', '=', etc."
            },
            modifiers = new
            {
                type = "array",
                items = new { type = "string", @enum = new[] { "ctrl", "shift", "alt" } },
                description = "Modifier keys to hold while pressing the key (e.g., [\"ctrl\", \"shift\"])"
            },
            @ref = new
            {
                type = "string",
                description = "Element ref to focus before pressing the key. If omitted, sends to the currently focused element."
            }
        },
        required = new[] { "key" }
    };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var keyName = GetStringArgument(arguments, "key");
        if (string.IsNullOrEmpty(keyName))
        {
            return ErrorResult("Missing required argument: key");
        }

        var modifiers = GetArgument<string[]>(arguments, "modifiers") ?? Array.Empty<string>();
        var refId = GetStringArgument(arguments, "ref");

        try
        {
            // Focus element if ref provided
            if (!string.IsNullOrEmpty(refId))
            {
                var element = _elementRegistry.GetElement(refId);
                if (element == null)
                {
                    return ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs.");
                }

                element.Focus();
                await Task.Delay(50);
            }

            var vk = MapKeyName(keyName);
            if (vk == null)
            {
                return ErrorResult($"Unknown key: '{keyName}'");
            }

            // Build modifier list
            var modifierKeys = new List<VirtualKeyShort>();
            foreach (var mod in modifiers)
            {
                var modKey = mod.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => VirtualKeyShort.CONTROL,
                    "shift" => VirtualKeyShort.SHIFT,
                    "alt" => VirtualKeyShort.ALT,
                    _ => (VirtualKeyShort?)null
                };
                if (modKey == null)
                {
                    return ErrorResult($"Unknown modifier: '{mod}'. Use 'ctrl', 'shift', or 'alt'.");
                }
                modifierKeys.Add(modKey.Value);
            }

            // Press the key with modifiers
            if (modifierKeys.Count > 0)
            {
                var allKeys = modifierKeys.Append(vk.Value).ToArray();
                Keyboard.TypeSimultaneously(allKeys);
            }
            else
            {
                Keyboard.Press(vk.Value);
            }

            // Build description
            var desc = string.Join("+", modifiers.Select(m => m.ToUpperInvariant()).Append(keyName));
            var target = string.IsNullOrEmpty(refId) ? "" : $" (focused {refId})";
            return TextResult($"Pressed {desc}{target}");
        }
        catch (Exception ex)
        {
            return ErrorResult($"Failed to press key: {ex.Message}");
        }
    }

    private static VirtualKeyShort? MapKeyName(string keyName)
    {
        // Single character keys
        if (keyName.Length == 1)
        {
            var ch = keyName[0];
            return ch switch
            {
                >= 'a' and <= 'z' => (VirtualKeyShort)((int)VirtualKeyShort.KEY_A + (ch - 'a')),
                >= 'A' and <= 'Z' => (VirtualKeyShort)((int)VirtualKeyShort.KEY_A + (ch - 'A')),
                >= '0' and <= '9' => (VirtualKeyShort)((int)VirtualKeyShort.KEY_0 + (ch - '0')),
                ' ' => VirtualKeyShort.SPACE,
                '+' or '=' => VirtualKeyShort.OEM_PLUS,
                '-' => VirtualKeyShort.OEM_MINUS,
                '.' => VirtualKeyShort.OEM_PERIOD,
                ',' => VirtualKeyShort.OEM_COMMA,
                '/' => VirtualKeyShort.OEM_2,
                ';' => VirtualKeyShort.OEM_1,
                '\'' => VirtualKeyShort.OEM_7,
                '[' => VirtualKeyShort.OEM_4,
                ']' => VirtualKeyShort.OEM_6,
                '\\' => VirtualKeyShort.OEM_5,
                '`' => VirtualKeyShort.OEM_3,
                _ => null
            };
        }

        // Named keys
        return keyName.ToLowerInvariant() switch
        {
            "enter" or "return" => VirtualKeyShort.ENTER,
            "escape" or "esc" => VirtualKeyShort.ESCAPE,
            "tab" => VirtualKeyShort.TAB,
            "space" => VirtualKeyShort.SPACE,
            "backspace" or "back" => VirtualKeyShort.BACK,
            "delete" or "del" => VirtualKeyShort.DELETE,
            "up" => VirtualKeyShort.UP,
            "down" => VirtualKeyShort.DOWN,
            "left" => VirtualKeyShort.LEFT,
            "right" => VirtualKeyShort.RIGHT,
            "home" => VirtualKeyShort.HOME,
            "end" => VirtualKeyShort.END,
            "pageup" => VirtualKeyShort.PRIOR,
            "pagedown" => VirtualKeyShort.NEXT,
            "insert" => VirtualKeyShort.INSERT,
            "f1" => VirtualKeyShort.F1,
            "f2" => VirtualKeyShort.F2,
            "f3" => VirtualKeyShort.F3,
            "f4" => VirtualKeyShort.F4,
            "f5" => VirtualKeyShort.F5,
            "f6" => VirtualKeyShort.F6,
            "f7" => VirtualKeyShort.F7,
            "f8" => VirtualKeyShort.F8,
            "f9" => VirtualKeyShort.F9,
            "f10" => VirtualKeyShort.F10,
            "f11" => VirtualKeyShort.F11,
            "f12" => VirtualKeyShort.F12,
            _ => null
        };
    }
}
