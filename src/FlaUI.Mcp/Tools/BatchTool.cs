using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Execute multiple actions in a single call. Each action delegates to the
/// underlying tool (windows_click, windows_find, etc.) so features like
/// modifier keys, hang detection, and transient-error retry come along for
/// free. Batch actions can capture a find result with `as: "name"` and
/// reference it from later actions with `ref: "@name"`.
/// </summary>
public class BatchTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    private readonly ClickTool _clickTool;
    private readonly InvokeTool _invokeTool;
    private readonly TypeTool _typeTool;
    private readonly FillTool _fillTool;
    private readonly SnapshotTool _snapshotTool;
    private readonly FindTool _findTool;

    private static readonly string[] SupportedActions =
        { "find", "click", "invoke", "type", "fill", "wait", "snapshot" };

    public BatchTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _clickTool = new ClickTool(elementRegistry);
        _invokeTool = new InvokeTool(elementRegistry);
        _typeTool = new TypeTool(elementRegistry);
        _fillTool = new FillTool(elementRegistry);
        _snapshotTool = new SnapshotTool(sessionManager, elementRegistry);
        _findTool = new FindTool(sessionManager, elementRegistry);
    }

    public override string Name => "windows_batch";

    public override string Description =>
        "Execute multiple actions in a single call. Much faster than individual MCP round-trips. " +
        "Each action delegates to the corresponding tool (windows_click, windows_find, etc.) so " +
        "tool features like modifier keys, hang detection, and retry are honored. Use " +
        "`as: \"name\"` on a find action to bind the first matched ref to an alias, then reference " +
        "it from a later action with `ref: \"@name\"`. Supported actions: find, click, invoke, " +
        "type, fill, wait, snapshot. ('wait' here is a fixed-millisecond sleep; for state-polling " +
        "use windows_wait as a separate tool call.)";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            actions = new
            {
                type = "array",
                description = "List of actions to execute in order. Each item is an object with " +
                              "an 'action' field plus that action's parameters.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new
                        {
                            type = "string",
                            @enum = SupportedActions,
                            description = "Which tool to invoke for this step"
                        },
                        @ref = new
                        {
                            type = "string",
                            description = "Element ref. Prefix with @ to reference an alias bound by a previous find action (e.g. \"@patientId\")."
                        },
                        @as = new
                        {
                            type = "string",
                            description = "On a find action, bind the first matched element's ref to this alias name. Subsequent actions can reference it via ref=\"@name\"."
                        },
                        handle = new
                        {
                            type = "string",
                            description = "Window handle (used by find, snapshot)"
                        },
                        name = new { type = "string", description = "Element name (find action)" },
                        automationId = new { type = "string", description = "Element AutomationId (find action)" },
                        role = new { type = "string", description = "Element role (find action)" },
                        text = new { type = "string", description = "Text to type (type action)" },
                        value = new { type = "string", description = "Value to fill (fill action)" },
                        button = new { type = "string", description = "Mouse button for click (left/right/middle)" },
                        doubleClick = new { type = "boolean", description = "Double-click (click action)" },
                        modifiers = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Modifier keys held during click (ctrl/shift/alt/win)"
                        },
                        ms = new { type = "integer", description = "Milliseconds for wait action (default 100)" },
                        backend = new
                        {
                            type = "string",
                            @enum = new[] { "uia3", "uia2" },
                            description = "Snapshot backend"
                        }
                    },
                    required = new[] { "action" }
                }
            },
            stopOnError = new
            {
                type = "boolean",
                description = "Stop executing if an action fails (default: true)"
            }
        },
        required = new[] { "actions" }
    };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        if (arguments == null || !arguments.Value.TryGetProperty("actions", out var actionsRaw))
        {
            return ErrorResult("Missing required argument: actions");
        }

        if (!TryNormalizeActionsArray(actionsRaw, out var actionsArray, out var normalizeError))
        {
            return ErrorResult(normalizeError!);
        }

        var stopOnError = GetBoolArgument(arguments, "stopOnError", true);

        var aliases = new Dictionary<string, string>();
        var results = new List<string>();
        var actions = actionsArray.EnumerateArray().ToList();

        for (int i = 0; i < actions.Count; i++)
        {
            var actionObj = actions[i];

            try
            {
                var actionType = actionObj.TryGetProperty("action", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                    ? typeProp.GetString()
                    : null;

                if (string.IsNullOrEmpty(actionType))
                {
                    results.Add($"{i + 1}. ERROR: action[{i}] missing required 'action' field");
                    if (stopOnError) { results.Add($"Stopped at action {i + 1} due to error"); break; }
                    continue;
                }

                if (!SupportedActions.Contains(actionType))
                {
                    results.Add($"{i + 1}. ERROR: action[{i}] has unknown action type '{actionType}'. Supported: {string.Join(", ", SupportedActions)}.");
                    if (stopOnError) { results.Add($"Stopped at action {i + 1} due to error"); break; }
                    continue;
                }

                var resolvedAction = ResolveAliases(actionObj, aliases, out var aliasError);
                if (aliasError != null)
                {
                    results.Add($"{i + 1}. ERROR: {aliasError}");
                    if (stopOnError) { results.Add($"Stopped at action {i + 1} due to error"); break; }
                    continue;
                }

                var resultText = await ExecuteActionAsync(actionType, resolvedAction);

                // For find actions, capture the first matched ref under the requested alias.
                if (actionType == "find"
                    && actionObj.TryGetProperty("as", out var asProp)
                    && asProp.ValueKind == JsonValueKind.String)
                {
                    var aliasName = asProp.GetString();
                    if (!string.IsNullOrEmpty(aliasName))
                    {
                        var match = Regex.Match(resultText, @"\[ref=(\w+)");
                        if (match.Success)
                        {
                            aliases[aliasName] = match.Groups[1].Value;
                        }
                    }
                }

                results.Add($"{i + 1}. {actionType}: {resultText}");
            }
            catch (Exception ex)
            {
                results.Add($"{i + 1}. ERROR: {ex.Message}");
                if (stopOnError)
                {
                    results.Add($"Stopped at action {i + 1} due to error");
                    break;
                }
            }
        }

        return TextResult(string.Join("\n", results));
    }

    /// <summary>
    /// Forgive callers who pass `actions` as a JSON-encoded string instead of
    /// a JSON array (a common LLM/serialization mistake). If actions is a
    /// string, try to parse it as JSON; if it parses to an array, use that.
    /// Returns false with a user-facing error otherwise.
    /// </summary>
    private static bool TryNormalizeActionsArray(JsonElement raw, out JsonElement actionsArray, out string? error)
    {
        actionsArray = raw;
        error = null;

        if (raw.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (raw.ValueKind == JsonValueKind.String)
        {
            var s = raw.GetString();
            if (string.IsNullOrWhiteSpace(s))
            {
                error = "'actions' was an empty string. Pass an array of action objects.";
                return false;
            }
            try
            {
                // Note: this JsonDocument is intentionally not disposed. It
                // owns the backing memory for the returned JsonElement, which
                // must outlive this call. GC will reclaim it once the parent
                // tool call returns.
                var parsed = JsonDocument.Parse(s);
                if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                {
                    error = "'actions' was a string but did not parse to a JSON array. Pass actions as an array of action objects (not a JSON-encoded string).";
                    return false;
                }
                actionsArray = parsed.RootElement;
                return true;
            }
            catch (JsonException ex)
            {
                error = $"'actions' was a string but did not parse as JSON: {ex.Message}. Pass actions as an array of action objects (not a JSON-encoded string).";
                return false;
            }
        }

        error = $"'actions' must be an array of action objects, got {raw.ValueKind}.";
        return false;
    }

    private async Task<string> ExecuteActionAsync(string actionType, JsonElement action)
    {
        return actionType switch
        {
            "wait" => ExecuteWait(action),
            "find" => await CallTool(_findTool, action),
            "click" => await CallTool(_clickTool, action),
            "invoke" => await CallTool(_invokeTool, action),
            "type" => await CallTool(_typeTool, action),
            "fill" => await CallTool(_fillTool, action),
            "snapshot" => await CallTool(_snapshotTool, action),
            _ => throw new InvalidOperationException($"Internal error: unhandled action {actionType}")
        };
    }

    private static async Task<string> CallTool(ToolBase tool, JsonElement action)
    {
        var result = await tool.ExecuteAsync(action);
        return result.Content.FirstOrDefault()?.Text ?? "";
    }

    private static string ExecuteWait(JsonElement action)
    {
        var ms = action.TryGetProperty("ms", out var msProp) && msProp.ValueKind == JsonValueKind.Number
            ? msProp.GetInt32()
            : 100;
        Thread.Sleep(ms);
        return $"Waited {ms}ms";
    }

    /// <summary>
    /// If the action's "ref" field starts with @, look up the alias and
    /// substitute the bound ref id. Returns a new JsonElement with the
    /// substitution applied (or the original if no substitution is needed).
    /// On unknown alias, sets error and returns the original element.
    /// </summary>
    private static JsonElement ResolveAliases(JsonElement action, IReadOnlyDictionary<string, string> aliases, out string? error)
    {
        error = null;
        if (!action.TryGetProperty("ref", out var refProp) || refProp.ValueKind != JsonValueKind.String)
        {
            return action;
        }

        var refValue = refProp.GetString();
        if (refValue == null || !refValue.StartsWith("@"))
        {
            return action;
        }

        var aliasName = refValue.Substring(1);
        if (!aliases.TryGetValue(aliasName, out var actualRef))
        {
            error = $"Unknown alias: '@{aliasName}'. Did a previous find action with as='{aliasName}' fail or return no matches?";
            return action;
        }

        var node = JsonNode.Parse(action.GetRawText())!.AsObject();
        node["ref"] = actualRef;
        return JsonDocument.Parse(node.ToJsonString()).RootElement;
    }
}
