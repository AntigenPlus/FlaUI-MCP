using FlaUI.Core.WindowsAPI;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Parses modifier-key name strings (the schema shape used by
/// windows_press_key and windows_click) into VirtualKeyShort values.
/// Shared so the modifier vocabulary is defined exactly once.
/// </summary>
public static class ModifierKeyParser
{
    public static bool TryParse(
        IReadOnlyList<string> modifiers,
        out List<VirtualKeyShort> result,
        out string? error)
    {
        result = new List<VirtualKeyShort>(modifiers.Count);
        foreach (var mod in modifiers)
        {
            var modKey = mod.ToLowerInvariant() switch
            {
                "ctrl" or "control" => VirtualKeyShort.CONTROL,
                "shift" => VirtualKeyShort.SHIFT,
                "alt" => VirtualKeyShort.ALT,
                "win" or "windows" or "meta" or "super" => VirtualKeyShort.LWIN,
                _ => (VirtualKeyShort?)null
            };
            if (modKey == null)
            {
                error = $"Unknown modifier: '{mod}'. Use 'ctrl', 'shift', 'alt', or 'win'.";
                return false;
            }
            result.Add(modKey.Value);
        }
        error = null;
        return true;
    }
}
