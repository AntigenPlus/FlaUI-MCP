using System.Text.Json;
using FlaUI.Core.AutomationElements;
using PlaywrightWindows.Mcp;
using PlaywrightWindows.Mcp.Core;
using PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Interactive scratch pad for testing FlaUI-MCP tools against real windows.
/// Keeps a single session alive so window handles and element refs are stable.
///
/// Usage: dotnet run --project tests/FlaUI.Mcp.Scratch [command] [args...]
///
/// Commands:
///   list                          - List all windows
///   snapshot &lt;handle&gt; [maxRows]   - Snapshot a window
///   find &lt;handle&gt; [--name X] [--role X] [--pattern X] [--depth N]
///   click &lt;ref&gt; [--method invoke|mouse|auto]
///   get-value &lt;ref&gt;               - Get element value
///   get-text &lt;ref&gt;                - Get element text
///   table &lt;ref&gt; [--rows 0-9] [--columns A,B]
///   press-key &lt;key&gt; [--mod ctrl,shift] [--ref X]
///   wait [--name X] [--role X] [--state exists|gone] [--timeout N]
///   launch &lt;app&gt;                  - Launch an application
///
/// Interactive mode (no args): enters a REPL loop.
/// </summary>
class Program
{
    static SessionManager _session = null!;
    static ElementRegistry _elements = null!;

    static async Task<int> Main(string[] args)
    {
        _session = new SessionManager();
        _elements = new ElementRegistry();

        try
        {
            if (args.Length > 0)
            {
                return await RunCommand(args) ? 0 : 1;
            }

            // Interactive REPL
            Console.WriteLine("FlaUI-MCP Scratch Pad — type 'help' for commands, 'quit' to exit");
            Console.WriteLine();

            while (true)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (line == "") continue;
                if (line is "quit" or "exit" or "q") break;
                if (line == "help") { PrintHelp(); continue; }

                var parts = ParseArgs(line);
                try
                {
                    await RunCommand(parts);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
                Console.WriteLine();
            }
        }
        finally
        {
            _session.Dispose();
        }

        return 0;
    }

    static async Task<bool> RunCommand(string[] args)
    {
        if (args.Length == 0) return false;
        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        switch (cmd)
        {
            case "list": return RunList();
            case "snapshot": return RunSnapshot(rest);
            case "click": return await RunClick(rest);
            case "get-text": return await RunGetText(rest);
            case "launch": return await RunLaunch(rest);
            // Tools that may not exist on all branches — resolved via reflection
            case "find":
            case "get-value":
            case "table":
            case "press-key":
            case "wait":
                return await RunDynamicTool(cmd, rest);
            default:
                Console.WriteLine($"Unknown command: {cmd}. Type 'help' for usage.");
                return false;
        }
    }

    static bool RunList()
    {
        var windows = _session.ListWindows();
        foreach (var (handle, title, process) in windows)
        {
            Console.WriteLine($"  {handle}: \"{title}\" ({process ?? "unknown"})");
        }
        Console.WriteLine($"  ({windows.Count} windows)");
        return true;
    }

    static bool RunSnapshot(string[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        var maxTableRows = 5;
        if (args.Length > 1 && int.TryParse(args[1], out var mr)) maxTableRows = mr;

        Window? window = null;
        if (!string.IsNullOrEmpty(handle))
        {
            window = _session.GetWindow(handle);
            if (window == null) { Console.WriteLine($"Window not found: {handle}"); return false; }
        }
        else
        {
            Console.WriteLine("Usage: snapshot <handle> [maxTableRows]");
            return false;
        }

        var builder = new SnapshotBuilder(_elements, maxTableRows: maxTableRows);
        var snapshot = builder.BuildSnapshot(handle!, window);
        Console.Write(snapshot);
        return true;
    }

    static async Task<bool> RunDynamicTool(string cmd, string[] args)
    {
        var opts = ParseOptions(args);
        var positional = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
        var toolArgs = new Dictionary<string, object?>();

        // Map command to tool class name and build arguments
        string toolClassName;
        switch (cmd)
        {
            case "find":
                toolClassName = "FindTool";
                if (positional != null) toolArgs["handle"] = positional;
                if (opts.TryGetValue("name", out var fn)) toolArgs["name"] = fn;
                if (opts.TryGetValue("role", out var fr)) toolArgs["role"] = fr;
                if (opts.TryGetValue("pattern", out var fp)) toolArgs["pattern"] = fp;
                if (opts.TryGetValue("depth", out var fd)) toolArgs["depth"] = int.Parse(fd);
                break;
            case "get-value":
                toolClassName = "GetValueTool";
                if (positional != null) toolArgs["ref"] = positional;
                break;
            case "table":
                toolClassName = "TableTool";
                if (positional != null) toolArgs["ref"] = positional;
                if (opts.TryGetValue("rows", out var tr)) toolArgs["rows"] = tr;
                if (opts.TryGetValue("columns", out var tc)) toolArgs["columns"] = tc;
                break;
            case "press-key":
                toolClassName = "PressKeyTool";
                if (positional != null) toolArgs["key"] = positional;
                if (opts.TryGetValue("mod", out var pm)) toolArgs["modifiers"] = pm.Split(',');
                if (opts.TryGetValue("ref", out var pr)) toolArgs["ref"] = pr;
                break;
            case "wait":
                toolClassName = "WaitTool";
                if (positional != null) toolArgs["handle"] = positional;
                if (opts.TryGetValue("name", out var wn)) toolArgs["name"] = wn;
                if (opts.TryGetValue("role", out var wr)) toolArgs["role"] = wr;
                if (opts.TryGetValue("state", out var ws)) toolArgs["state"] = ws;
                if (opts.TryGetValue("timeout", out var wt)) toolArgs["timeout"] = int.Parse(wt);
                break;
            default:
                Console.WriteLine($"Unknown dynamic tool: {cmd}");
                return false;
        }

        // Find the tool type by name
        var asm = typeof(ToolBase).Assembly;
        var toolType = asm.GetTypes().FirstOrDefault(t =>
            t.Name == toolClassName && typeof(ToolBase).IsAssignableFrom(t));

        if (toolType == null)
        {
            Console.WriteLine($"Tool '{toolClassName}' not available on this branch.");
            return false;
        }

        // Instantiate — try constructors with (SessionManager, ElementRegistry), (ElementRegistry), (SessionManager)
        ToolBase? tool = null;
        try
        {
            tool = (ToolBase?)Activator.CreateInstance(toolType, _session, _elements)
                ?? (ToolBase?)Activator.CreateInstance(toolType, _elements)
                ?? (ToolBase?)Activator.CreateInstance(toolType, _session);
        }
        catch
        {
            try { tool = (ToolBase?)Activator.CreateInstance(toolType, _elements); }
            catch
            {
                try { tool = (ToolBase?)Activator.CreateInstance(toolType, _session); }
                catch { }
            }
        }

        if (tool == null)
        {
            Console.WriteLine($"Could not instantiate '{toolClassName}'.");
            return false;
        }

        return await CallTool(tool, toolArgs);
    }

    static async Task<bool> RunClick(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Usage: click <ref> [--method invoke|mouse|auto]"); return false; }
        var opts = ParseOptions(args);
        var toolArgs = new Dictionary<string, object?> { ["ref"] = args[0] };
        if (opts.TryGetValue("method", out var m)) toolArgs["method"] = m;

        var tool = new ClickTool(_elements);
        return await CallTool(tool, toolArgs);
    }

    static async Task<bool> RunGetText(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Usage: get-text <ref>"); return false; }
        var tool = new GetTextTool(_elements);
        return await CallTool(tool, new Dictionary<string, object?> { ["ref"] = args[0] });
    }

    static async Task<bool> RunLaunch(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Usage: launch <app>"); return false; }
        var tool = new LaunchTool(_session);
        return await CallTool(tool, new Dictionary<string, object?> { ["app"] = string.Join(" ", args) });
    }

    static async Task<bool> CallTool(ToolBase tool, Dictionary<string, object?> args)
    {
        var json = JsonSerializer.Serialize(args, McpProtocol.JsonOptions);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        var result = await tool.ExecuteAsync(element);

        foreach (var content in result.Content)
        {
            if (content.Type == "text" && content.Text != null)
            {
                Console.WriteLine(content.Text);
            }
            else if (content.Type == "image")
            {
                Console.WriteLine($"[image: {content.MimeType}, {content.Data?.Length ?? 0} chars base64]");
            }
        }

        if (result.IsError == true)
        {
            Console.WriteLine("(error)");
            return false;
        }
        return true;
    }

    static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--"))
            {
                opts[args[i][2..]] = args[i + 1];
                i++;
            }
        }
        return opts;
    }

    static string[] ParseArgs(string line)
    {
        var args = new List<string>();
        var current = "";
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0) { args.Add(current); current = ""; }
                continue;
            }
            current += ch;
        }
        if (current.Length > 0) args.Add(current);
        return args.ToArray();
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"Commands:
  list                          List all windows
  snapshot <handle> [maxRows]   Snapshot a window (default maxRows=5)
  find <handle> [--name X] [--role X] [--pattern X] [--depth N]
  click <ref> [--method invoke|mouse|auto]
  get-value <ref>               Get element value
  get-text <ref>                Get element text
  table <ref> [--rows 0-9] [--columns A,B]
  press-key <key> [--mod ctrl,shift] [--ref X]
  wait [--name X] [--role X] [--state exists|gone] [--timeout N]
  launch <app>                  Launch an application
  help                          Show this help
  quit                          Exit");
    }
}
