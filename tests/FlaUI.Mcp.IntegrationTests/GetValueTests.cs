using System.Diagnostics;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_get_value tool using the test apps.
/// Tests each value source: Value pattern, Toggle, SelectionItem, RangeValue, and Name fallback.
/// </summary>
[Collection("TestApps")]
public class GetValueTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GetValueTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private async Task<string> NavigateToTabAndFind(string handle, string tabName, string elementName)
    {
        var snapshot = _fixture.TakeSnapshot(handle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, tabName);
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(100);
            var found = _fixture.FindRefByName(handle, elementName);
            if (found != null) return found;
        }

        Assert.Fail($"Element \"{elementName}\" not found after navigating to \"{tabName}\" tab.");
        return "";
    }

    [Fact]
    public async Task GetValue_TextBox_ReturnsValuePattern()
    {
        var nameRef = await NavigateToTabAndFind(_fixture.WinFormsHandle, "Forms", "Name");

        // Fill a value first
        var fillTool = new FillTool(_fixture.Elements);
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "Test Value" });

        // Get value should return it via Value pattern
        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = nameRef });
        _output.WriteLine($"TextBox value: '{result}'");
        Assert.Equal("Test Value", result);

        // Clean up
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "" });
    }

    [Fact]
    public async Task GetValue_ReadOnlyTextBox_ReturnsValue()
    {
        var resultRef = await NavigateToTabAndFind(_fixture.WinFormsHandle, "Forms", "Result");

        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = resultRef });
        _output.WriteLine($"ReadOnly TextBox value: '{result}'");
        Assert.Equal("Computed value here", result);
    }

    [Fact]
    public async Task GetValue_Checkbox_ReturnsToggleState()
    {
        // Navigate to Buttons tab and find the checkbox
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var cbRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Enable the button below");
        Assert.NotNull(cbRef);

        var tool = new GetValueTool(_fixture.Elements);

        // Should be unchecked initially
        var result1 = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Checkbox value (before): '{result1}'");
        Assert.Equal("unchecked", result1);

        // Toggle it
        await _fixture.CallTool(clickTool, new { @ref = cbRef });
        await Task.Delay(100);

        // Re-find after state change
        cbRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Enable the button below");
        Assert.NotNull(cbRef);

        var result2 = await _fixture.CallTool(tool, new { @ref = cbRef });
        _output.WriteLine($"Checkbox value (after): '{result2}'");
        Assert.Equal("checked", result2);

        // Toggle back
        await _fixture.CallTool(clickTool, new { @ref = cbRef });
    }

    [Fact]
    public async Task GetValue_RadioButton_ReturnsSelectionState()
    {
        // Navigate to Buttons tab
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);

        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var radioARef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Option A");
        var radioBRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Option B");
        Assert.NotNull(radioARef);
        Assert.NotNull(radioBRef);

        var tool = new GetValueTool(_fixture.Elements);

        var resultA = await _fixture.CallTool(tool, new { @ref = radioARef });
        _output.WriteLine($"Radio A value: '{resultA}'");

        var resultB = await _fixture.CallTool(tool, new { @ref = radioBRef });
        _output.WriteLine($"Radio B value: '{resultB}'");

        // One should be selected, the other not
        Assert.True(
            (resultA == "selected" && resultB == "unselected") ||
            (resultA == "checked" && resultB == "unchecked"),
            $"Expected one selected/checked and one not. Got A='{resultA}', B='{resultB}'");
    }

    [Fact]
    public async Task GetValue_Slider_ReturnsRangeValue()
    {
        var sliderRef = await NavigateToTabAndFind(_fixture.WinFormsHandle, "Forms", "Volume");

        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = sliderRef });
        _output.WriteLine($"Slider value: '{result}'");

        // Slider is initialized to 50
        Assert.Equal("50", result);
    }

    [Fact]
    public async Task GetValue_Button_FallsBackToName()
    {
        // Navigate to Buttons tab to ensure the button is visible
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var tabRef = TestAppFixture.FindRefInSnapshot(snapshot, "Buttons");
        Assert.NotNull(tabRef);
        var clickTool = new ClickTool(_fixture.Elements);
        await _fixture.CallTool(clickTool, new { @ref = tabRef });
        await Task.Delay(100);

        var buttonRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Click Me");
        Assert.NotNull(buttonRef);

        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = buttonRef });
        _output.WriteLine($"Button value (name fallback): '{result}'");
        Assert.Equal("Click Me", result);
    }

    [Fact]
    public async Task GetValue_InvalidRef_ReturnsError()
    {
        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = "w999e999" });
        _output.WriteLine($"Invalid ref result: '{result}'");
        Assert.Contains("Element not found", result);
    }

    [Fact]
    public async Task GetValue_Wpf_TextBox_ReturnsValue()
    {
        var nameRef = await NavigateToTabAndFind(_fixture.WpfHandle, "Forms", "Name");

        // Fill a value
        var fillTool = new FillTool(_fixture.Elements);
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "WPF Test" });

        var tool = new GetValueTool(_fixture.Elements);
        var result = await _fixture.CallTool(tool, new { @ref = nameRef });
        _output.WriteLine($"WPF TextBox value: '{result}'");
        Assert.Equal("WPF Test", result);

        // Clean up
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "" });
    }
}
