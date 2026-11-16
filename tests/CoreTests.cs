using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SidebarSorter;

internal static class CoreTests
{
    private static readonly List<object> results = [];
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task Check(string name, Func<Task> action)
    {
        try { await action(); results.Add(new { name, passed = true }); }
        catch (Exception error) { results.Add(new { name, passed = false, error = error.ToString() }); }
    }

    private static Task CheckAction(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });

    private static async Task<int> Main(string[] arguments)
    {
        string source = Path.GetFullPath(arguments[0]);
        string report = Path.GetFullPath(arguments[1]);
        string temporary = Path.Combine(Path.GetTempPath(), "sidebar-sorter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        Localization.SetLanguage("en");
        await CheckAction("name-ascending", () => AssertOrder([new("s", "上海"), new("b", "北京"), new("a", "苹果")], 0, ["b", "a", "s"]));
        await CheckAction("name-descending", () => AssertOrder([new("s", "上海"), new("b", "北京"), new("a", "苹果")], 1, ["s", "a", "b"]));
        await CheckAction("natural-ascending", () => AssertOrder([new("10", "Item 10"), new("2", "Item 2"), new("1", "Item 1")], 2, ["1", "2", "10"]));
        await CheckAction("natural-descending", () => AssertOrder([new("10", "Item 10"), new("2", "Item 2"), new("1", "Item 1")], 3, ["10", "2", "1"]));
        await CheckAction("newest-first-missing-last", () => AssertOrder([new("missing", "Missing"), new("old", "Old", 1), new("new", "New", 2)], 4, ["new", "old", "missing"]));
        await CheckAction("oldest-first-missing-last", () => AssertOrder([new("missing", "Missing"), new("old", "Old", 1), new("new", "New", 2)], 5, ["old", "new", "missing"]));
        await CheckAction("manual-order-retained", () => AssertOrder([new("two", "B"), new("one", "A")], 6, ["two", "one"]));
        await CheckAction("stable-duplicate-names", () => AssertOrder([new("first", "ALPHA"), new("second", "alpha"), new("third", "beta")], 0, ["first", "second", "third"]));
        await CheckAction("both-defaults-name-ascending", () =>
        {
            ToolSettings settings = new ToolSettingsStore(Path.Combine(temporary, "new-settings")).Load();
            Require(settings.ProjectSortMode == 0 && settings.ChatSortMode == 0, "Both default modes must be name ascending.");
        });
        await CheckAction("language-switch-does-not-change-sort-rule", () =>
        {
            var store = new ToolSettingsStore(Path.Combine(temporary, "settings"));
            var settings = new ToolSettings { Language = "en", ProjectSortMode = 2, ChatSortMode = 5 };
            store.Save(settings);
            Require(store.Load() == settings, "Settings round trip.");
            settings = settings with { Language = "zh-CN" };
            store.Save(settings);
            Require(store.Load() == settings, "Changing the language must retain sorting choices.");
        });
        await CheckAction("live-language-dictionary", () =>
        {
            Localization.SetLanguage("en");
            Require(Localization.Text("名称升序") == "Name A to Z", "English mode.");
            Localization.SetLanguage("zh-CN");
            Require(Localization.Text("名称升序") == "名称升序", "Chinese mode.");
            Localization.SetLanguage("en");
            Require(Localization.SourceText("Refresh") == "刷新", "Reverse lookup for existing captions.");
        });
        await CheckAction("all-explicit-ui-messages-have-english", () =>
        {
            var keys = new HashSet<string>();
            foreach (string file in Directory.GetFiles(source, "*.cs"))
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "Localization\\.Text\\((\"(?:\\\\.|[^\"\\\\])*\")\\)"))
                    keys.Add(JsonSerializer.Deserialize<string>(match.Groups[1].Value)!);
            string[] missing = keys.Where(key => !Localization.HasEnglish(key)).ToArray();
            Require(missing.Length == 0, "Missing translations: " + string.Join(" | ", missing));
        });
        await CheckAction("pinned-slots-and-parent-membership", VerifyWorkspace);
        await CheckAction("current-history-schema-readonly-attached-filter", () => VerifyHistory(Path.Combine(temporary, "current"), true));
        await CheckAction("older-history-schema-readonly", () => VerifyHistory(Path.Combine(temporary, "older"), false));
        await Check("native-pipe-framing-and-split-response", VerifyPipeAsync);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        string json = JsonSerializer.Serialize(new
        {
            operatingSystem = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            tests = results,
            success = results.All(result => JsonSerializer.SerializeToNode(result)!["passed"]!.GetValue<bool>()),
            checkedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(report, json);
        Console.WriteLine(json);
        return results.All(result => JsonSerializer.SerializeToNode(result)!["passed"]!.GetValue<bool>()) ? 0 : 1;
    }

    private static void AssertOrder(List<SortRow> rows, int mode, string[] expected)
    {
        SorterService.Sort(rows, mode);
        Require(rows.Select(row => row.Id).SequenceEqual(expected), "Unexpected sorted order.");
    }

    private static void VerifyWorkspace()
    {
        var pinned = new SortRow("pinned", "Pinned", IsPinned: true);
        var group = new ProjectGroup
        {
            Scope = new("project", "project", "Project"), Row = new("project", "Project"),
            Chats = [new("b", "Beta"), pinned, new("a", "Alpha")], OriginalChats = [], OriginalPositions = new(),
            CanReorder = true, IsLoaded = true
        };
        WorkspacePreview.SortGroupChats(group, 0);
        Require(group.Chats.Select(row => row.Id).SequenceEqual(["a", "pinned", "b"]), "Pinned slot.");
        Require(group.Scope.Id == "project", "Project membership.");
    }

    private static void VerifyHistory(string directory, bool currentSchema)
    {
        RuntimeHelpers.RunClassConstructor(typeof(LocalHistoryReader).TypeHandle);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".codex-global-state.json"), "{}");
        string databaseFile = Path.Combine(directory, "state_1.sqlite");
        Require(sqlite3_open(databaseFile, out IntPtr database, 0x10006, IntPtr.Zero) == 0, "Create fixture database.");
        try
        {
            string parent = currentSchema ? ", attached_thread_parent_id TEXT" : "";
            string sql = "CREATE TABLE threads(id TEXT,name TEXT,title TEXT,updated_at_ms INTEGER,updated_at INTEGER,project_id TEXT,archived INTEGER,source TEXT,agent_path TEXT" + parent + ");"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('parent','Parent',1,0,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('root','Root',2,0,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('archived','Archived',3,1,'vscode');";
            if (currentSchema)
                sql += "UPDATE threads SET attached_thread_parent_id='' WHERE id='root';"
                    + "INSERT INTO threads(id,title,updated_at,archived,source,attached_thread_parent_id) VALUES('child','Child',4,0,'vscode','parent');";
            Require(sqlite3_exec(database, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error) == 0, Marshal.PtrToStringUTF8(error) ?? "Create fixture rows.");
        }
        finally { sqlite3_close(database); }
        string before = Convert.ToBase64String(File.ReadAllBytes(databaseFile));
        List<JsonObject> rows = LocalHistoryReader.Read(directory, new JsonObject { ["threads"] = new JsonArray(), ["pinnedThreads"] = new JsonArray() });
        Require(rows.Select(row => row["id"]!.GetValue<string>()).ToHashSet().SetEquals(["parent", "root"]), "Standalone active rows.");
        Require(Convert.ToBase64String(File.ReadAllBytes(databaseFile)) == before, "History bytes stay read-only.");
    }

    private static async Task VerifyPipeAsync()
    {
        string pipeName = "sidebar-sorter-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task accepting = server.WaitForConnectionAsync();
        await stream.ConnectAsync(3000);
        await accepting;
        using var bridge = new NativeBridge(stream, "fixture-caller");
        bridge.Tools.Add("list_projects");
        Task<JsonObject> call = bridge.CallAsync("list_projects", new JsonObject());
        var header = new byte[4];
        await server.ReadExactlyAsync(header);
        var request = new byte[BitConverter.ToInt32(header)];
        await server.ReadExactlyAsync(request);
        JsonObject parsed = JsonNode.Parse(request)!.AsObject();
        Require(parsed["method"]!.GetValue<string>() == "tools/call", "Native tool entry point.");
        Require(parsed["params"]?["tool"]?.GetValue<string>() == "list_projects", "Native tool name.");
        byte[] response = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["id"] = parsed["id"]!.DeepClone(), ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["success"] = true,
                ["contentItems"] = new JsonArray(new JsonObject { ["type"] = "inputText", ["text"] = "{\"projects\":[]}" })
            }
        }.ToJsonString());
        await server.WriteAsync(BitConverter.GetBytes(response.Length).AsMemory(0, 2));
        await server.WriteAsync(BitConverter.GetBytes(response.Length).AsMemory(2, 2));
        await server.WriteAsync(response.AsMemory(0, 3));
        await server.WriteAsync(response.AsMemory(3));
        Require((await call)["projects"]!.AsArray().Count == 0, "Split native response.");
    }

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);
}
