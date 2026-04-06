using System.Text.Json;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Get the programmatic value of an element (Value pattern, Toggle state, Selection state, RangeValue, etc.)
/// </summary>
public class GetValueTool : ToolBase
{
    private readonly ElementRegistry _elementRegistry;

    public GetValueTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_get_value";

    public override string Description =>
        "Get the programmatic value of an element. Reads the Value pattern for inputs, " +
        "Toggle state for checkboxes, Selection state for list items, and RangeValue for " +
        "sliders/progress bars. Distinct from windows_get_text which returns display text.";

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

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var refId = GetStringArgument(arguments, "ref");
        if (string.IsNullOrEmpty(refId))
        {
            return Task.FromResult(ErrorResult("Missing required argument: ref"));
        }

        var element = _elementRegistry.GetElement(refId);
        if (element == null)
        {
            return Task.FromResult(ErrorResult($"Element not found: {refId}. Run windows_snapshot to refresh element refs."));
        }

        try
        {
            // 1. Value pattern (text inputs, combo boxes, etc.)
            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
                return Task.FromResult(TextResult(value));
            }

            // 2. Toggle pattern (checkboxes, toggle buttons)
            if (element.Patterns.Toggle.IsSupported)
            {
                var toggleState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
                var stateText = toggleState switch
                {
                    ToggleState.On => "checked",
                    ToggleState.Off => "unchecked",
                    ToggleState.Indeterminate => "indeterminate",
                    _ => toggleState.ToString()
                };
                return Task.FromResult(TextResult(stateText));
            }

            // 3. SelectionItem pattern (list items, radio buttons)
            if (element.Patterns.SelectionItem.IsSupported)
            {
                var isSelected = element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
                return Task.FromResult(TextResult(isSelected ? "selected" : "unselected"));
            }

            // 4. RangeValue pattern (sliders, progress bars, spinners)
            if (element.Patterns.RangeValue.IsSupported)
            {
                var rangeValue = element.Patterns.RangeValue.Pattern.Value.ValueOrDefault;
                return Task.FromResult(TextResult(rangeValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            // 5. Fall back to Name property
            var name = element.Properties.Name.ValueOrDefault ?? "";
            return Task.FromResult(TextResult(name));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to get value from {refId}: {ex.Message}"));
        }
    }
}
