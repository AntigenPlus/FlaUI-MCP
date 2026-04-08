using System.Text.Json;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for windows_batch (#39). Covers the new find/invoke actions, the
/// as/@alias scheme, error messaging for unknown action types, and the
/// double-encoded actions-array forgiveness.
/// </summary>
[Collection("TestApps")]
public class BatchTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BatchTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private BatchTool NewTool() => new(_fixture.Session, _fixture.Elements);

    [Fact]
    public async Task Batch_UnknownActionType_ReturnsClearError()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "fly" }
            }
        });
        _output.WriteLine(result);

        Assert.Contains("unknown action type 'fly'", result);
        Assert.Contains("Supported:", result);
        Assert.Contains("find", result);
        Assert.Contains("click", result);
    }

    [Fact]
    public async Task Batch_MissingActionField_ReturnsClearError()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { @ref = "w1e5" }
            }
        });
        _output.WriteLine(result);
        Assert.Contains("missing required 'action' field", result);
    }

    [Fact]
    public async Task Batch_ActionsAsJsonString_IsForgiven()
    {
        // Some MCP clients (and LLMs) double-encode array arguments as a
        // JSON string. The tool should parse the string and proceed.
        var actionsJson = JsonSerializer.Serialize(new object[]
        {
            new { action = "wait", ms = 10 }
        });

        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new { actions = actionsJson });
        _output.WriteLine(result);

        Assert.Contains("Waited 10ms", result);
        Assert.DoesNotContain("requires an element of type", result);
    }

    [Fact]
    public async Task Batch_ActionsAsNonArrayString_ReturnsClearError()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new { actions = "not json at all" });
        _output.WriteLine(result);
        Assert.Contains("'actions'", result);
        Assert.Contains("string", result);
    }

    [Fact]
    public async Task Batch_FindWithAlias_BoundsRefForLaterAction()
    {
        // Issue #39's headline workflow: find then act on the result without
        // a separate MCP round trip.
        await _fixture.NavigateToTabAndFind(_fixture.WinFormsHandle, "Forms", "Name");

        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "find", handle = _fixture.WinFormsHandle, automationId = "NameTextBox", @as = "name" },
                new { action = "fill", @ref = "@name", value = "BatchedAlpha" }
            }
        });
        _output.WriteLine(result);

        Assert.Contains("find:", result);
        Assert.Contains("fill:", result);
        Assert.DoesNotContain("ERROR", result);
        Assert.DoesNotContain("Unknown alias", result);

        // Verify the field actually got the value.
        var nameRef = _fixture.FindRefByName(_fixture.WinFormsHandle, "Name");
        Assert.NotNull(nameRef);
        var getTool = new GetTextTool(_fixture.Elements);
        var text = await _fixture.CallTool(getTool, new { @ref = nameRef });
        Assert.Equal("BatchedAlpha", text);

        // Cleanup
        var fillTool = new FillTool(_fixture.Elements);
        await _fixture.CallTool(fillTool, new { @ref = nameRef, value = "" });
    }

    [Fact]
    public async Task Batch_UnknownAlias_ReturnsClearError()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "click", @ref = "@neverDefined" }
            }
        });
        _output.WriteLine(result);
        Assert.Contains("Unknown alias: '@neverDefined'", result);
    }

    [Fact]
    public async Task Batch_InvokeAction_DelegatesToInvokeTool()
    {
        // The pre-#32 BatchTool only knew about 'click' and used UIA Invoke
        // for it. Verify the new 'invoke' action explicitly delegates to
        // InvokeTool and returns its characteristic result text.
        await _fixture.NavigateToTabAndFind(_fixture.WinFormsHandle, "Buttons", "Click Me");

        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "find", handle = _fixture.WinFormsHandle, automationId = "ClickMeButton", @as = "btn" },
                new { action = "invoke", @ref = "@btn" }
            }
        });
        _output.WriteLine(result);

        Assert.Contains("UIA Invoke", result);
        Assert.Contains("Click Me", result);
    }

    [Fact]
    public async Task Batch_ClickWithModifiers_PassesThroughToClickTool()
    {
        // Verify modifier keys (#34) are honored when click runs in a batch.
        // The Modifier Test Button captures Control.ModifierKeys into a label.
        await _fixture.NavigateToTabAndFind(_fixture.WinFormsHandle, "Buttons", "Modifier Test Button");

        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "find", handle = _fixture.WinFormsHandle, automationId = "ModifierTestButton", @as = "btn" },
                new { action = "click", @ref = "@btn", modifiers = new[] { "ctrl" } }
            }
        });
        _output.WriteLine(result);

        Assert.Contains("[ctrl]", result);

        await Task.Delay(100);
        var snapshot = _fixture.TakeSnapshot(_fixture.WinFormsHandle);
        var line = snapshot.Split('\n').FirstOrDefault(l => l.Contains("Last click modifiers"));
        Assert.NotNull(line);
        Assert.Contains("Control", line);
    }

    [Fact]
    public async Task Batch_StopOnError_StopsAtFirstError()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "wait", ms = 10 },
                new { action = "fly" },
                new { action = "wait", ms = 10 }
            },
            stopOnError = true
        });
        _output.WriteLine(result);

        Assert.Contains("1. wait: Waited 10ms", result);
        Assert.Contains("unknown action type 'fly'", result);
        Assert.Contains("Stopped at action 2", result);
        // The third action should NOT have run.
        Assert.DoesNotContain("3. wait", result);
    }

    [Fact]
    public async Task Batch_StopOnErrorFalse_ContinuesPastErrors()
    {
        var tool = NewTool();
        var result = await _fixture.CallTool(tool, new
        {
            actions = new object[]
            {
                new { action = "wait", ms = 10 },
                new { action = "fly" },
                new { action = "wait", ms = 10 }
            },
            stopOnError = false
        });
        _output.WriteLine(result);

        Assert.Contains("1. wait: Waited 10ms", result);
        Assert.Contains("unknown action type 'fly'", result);
        Assert.Contains("3. wait: Waited 10ms", result);
        Assert.DoesNotContain("Stopped at action", result);
    }
}
