using System.Text.Json;
using PlaywrightWindows.Mcp;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;
using Xunit.Abstractions;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// Tests for the UIA2 backend fallback on windows_snapshot.
/// </summary>
[Collection("TestApps")]
public class Uia2FallbackTests
{
    private readonly TestAppFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Uia2FallbackTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Uia3_Snapshot_WinForms_ReturnsContent()
    {
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var json = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle, backend = "uia3" }, McpProtocol.JsonOptions);
        var result = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json)).Result;
        var text = result.Content.FirstOrDefault()?.Text ?? "";
        _output.WriteLine($"UIA3 snapshot length: {text.Length}");
        _output.WriteLine(text[..Math.Min(500, text.Length)]);

        Assert.Contains("FlaUI-MCP Test App", text);
    }

    [Fact]
    public void Uia2_Snapshot_WinForms_ReturnsContent()
    {
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);
        var json = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle, backend = "uia2" }, McpProtocol.JsonOptions);
        var result = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json)).Result;
        var text = result.Content.FirstOrDefault()?.Text ?? "";
        _output.WriteLine($"UIA2 snapshot length: {text.Length}");
        _output.WriteLine(text[..Math.Min(500, text.Length)]);

        Assert.Contains("FlaUI-MCP Test App", text);
    }

    [Fact]
    public void Uia2_Snapshot_MayDifferFromUia3()
    {
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);

        var json3 = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle, backend = "uia3" }, McpProtocol.JsonOptions);
        var result3 = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json3)).Result;
        var text3 = result3.Content.FirstOrDefault()?.Text ?? "";

        var json2 = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle, backend = "uia2" }, McpProtocol.JsonOptions);
        var result2 = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json2)).Result;
        var text2 = result2.Content.FirstOrDefault()?.Text ?? "";

        _output.WriteLine($"UIA3 length: {text3.Length}, UIA2 length: {text2.Length}");
        _output.WriteLine($"Snapshots identical: {text3 == text2}");

        // Both should contain the window, but may have structural differences
        Assert.Contains("FlaUI-MCP Test App", text3);
        Assert.Contains("FlaUI-MCP Test App", text2);
        // It's okay if they differ — the point is both work
    }

    [Fact]
    public void Default_Backend_IsUia3()
    {
        var tool = new SnapshotTool(_fixture.Session, _fixture.Elements);

        // No backend specified
        var jsonDefault = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle }, McpProtocol.JsonOptions);
        var resultDefault = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(jsonDefault)).Result;
        var textDefault = resultDefault.Content.FirstOrDefault()?.Text ?? "";

        // Explicit UIA3
        var json3 = JsonSerializer.Serialize(new { handle = _fixture.WinFormsHandle, backend = "uia3" }, McpProtocol.JsonOptions);
        var result3 = tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json3)).Result;
        var text3 = result3.Content.FirstOrDefault()?.Text ?? "";

        _output.WriteLine($"Default length: {textDefault.Length}, UIA3 length: {text3.Length}");

        // Default and explicit UIA3 should produce the same result
        Assert.Equal(textDefault, text3);
    }
}
