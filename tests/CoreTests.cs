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
        await CheckAction("name-ascending", () => AssertOrder([new("s", "\u4e0a\u6d77"), new("b", "\u5317\u4eac"), new("a", "\u82f9\u679c")], 0, ["b", "a", "s"]));
        await CheckAction("name-descending", () => AssertOrder([new("s", "\u4e0a\u6d77"), new("b", "\u5317\u4eac"), new("a", "\u82f9\u679c")], 1, ["s", "a", "b"]));
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
            Require(settings.Language == "en", "New installations start in English.");
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
            Require(Localization.Text("Name A to Z") == "Name A to Z", "English mode.");
            Localization.SetLanguage("zh-CN");
            Require(Localization.Text("Name A to Z") == "\u540d\u79f0\u5347\u5e8f", "Simplified Chinese remains selectable.");
            Localization.SetLanguage("en");
            Require(Localization.SourceText("Refresh") == "Refresh", "Reverse lookup for existing captions.");
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
        await CheckAction("twelve-complete-language-options", () =>
        {
            using Stream resource = typeof(Localization).Assembly.GetManifestResourceStream("SidebarSorter.English")!;
            var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
            Require(Localization.Choices.Count == 13, "Twelve languages and the system option are required.");
            foreach (LanguageChoice choice in Localization.Choices.Where(choice => choice.Code != "system"))
            {
                Localization.SetLanguage(choice.Code);
                foreach (string message in messages.Keys)
                {
                    Require(Localization.HasTranslation(choice.Code, message), choice.Code + " is missing " + message);
                    Require(!string.IsNullOrWhiteSpace(Localization.Text(message)), choice.Code + " has an empty message.");
                }
                Require(Localization.IsRightToLeft == (choice.Code == "ar"), "Arabic uses right-to-left layout.");
            }
            Localization.SetLanguage("en");
        });
        await CheckAction("regional-system-languages", () =>
        {
            foreach (var (culture, expected) in new (string, string)[]
            {
                ("zh-CN", "zh-CN"), ("zh-Hant-HK", "zh-TW"), ("zh-TW", "zh-TW"),
                ("ja-JP", "ja"), ("ko-KR", "ko"), ("fr-CA", "fr"), ("de-DE", "de"),
                ("es-MX", "es"), ("pt-BR", "pt"), ("it-IT", "it"), ("ru-RU", "ru"),
                ("ar-SA", "ar"), ("en-GB", "en"), ("sv-SE", "en")
            }) Require(Localization.ResolveCulture(culture) == expected, "System language mapping failed for " + culture);
        });
        await CheckAction("all-language-settings-retain-sort-choices", () =>
        {
            var store = new ToolSettingsStore(Path.Combine(temporary, "multilingual-settings"));
            foreach (LanguageChoice choice in Localization.Choices)
            {
                var settings = new ToolSettings { Language = choice.Code, ProjectSortMode = 2, ChatSortMode = 5, AutomaticNameSorting = false };
                store.Save(settings);
                Require(store.Load() == settings, "Language and sort choices must survive reopening.");
            }
        });
        await CheckAction("pinned-slots-and-parent-membership", VerifyWorkspace);
        await CheckAction("current-history-schema-readonly-attached-filter", () => VerifyHistory(Path.Combine(temporary, "current"), 5));
        await CheckAction("older-history-schema-readonly", () => VerifyHistory(Path.Combine(temporary, "older"), 4));
        await CheckAction("initial-history-schema-readonly", () => VerifyHistory(Path.Combine(temporary, "initial"), 0));
        await CheckAction("agent-path-history-schema-readonly", () => VerifyHistory(Path.Combine(temporary, "agent-path"), 1));
        await CheckAction("millisecond-history-schema-readonly", () => VerifyHistory(Path.Combine(temporary, "millisecond"), 2));
        await CheckAction("named-history-schema-before-projects-readonly", () => VerifyHistory(Path.Combine(temporary, "named"), 3));
        await CheckAction("missing-native-capability-preflight", () =>
        {
            using var stream = new MemoryStream();
            using var bridge = new NativeBridge(stream, "fixture-caller");
            bridge.Tools.Add("reorder_sidebar_projects");
            bool rejected = false;
            try { bridge.RequireTools("reorder_sidebar_projects", "move_thread_to_project"); }
            catch (InvalidOperationException error) when (error.Message.Contains("move_thread_to_project", StringComparison.Ordinal)) { rejected = true; }
            Require(rejected && stream.Length == 0, "Missing capabilities must be detected before a request is written.");
            bridge.RequireTools("reorder_sidebar_projects");
        });
        await Check("native-pipe-framing-and-split-response", () => VerifyPipeAsync());
        await Check("native-rate-limit-status-and-retry-after", () => VerifyPipeAsync(rateLimited: true));
        await CheckAction("changed-native-argument-contract-is-rejected", () =>
        {
            using var stream = new MemoryStream();
            using var bridge = new NativeBridge(stream, "fixture-caller");
            bridge.ReadToolCatalog(JsonNode.Parse("""
                {"tools":[{"namespace":"codex_app","name":"reorder_sidebar_projects","inputSchema":
                {"type":"object","properties":{"projectIds":{"type":"array","items":{"type":"string"}}},
                "required":["projectIds"],"additionalProperties":false}}]}
                """)!.AsObject());
            bridge.RequireArguments("reorder_sidebar_projects", new JsonObject { ["projectIds"] = SorterService.StringArray(["one"]) });
            bool rejected = false;
            try { bridge.RequireArguments("reorder_sidebar_projects", new JsonObject { ["projectIds"] = new JsonArray(7) }); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && stream.Length == 0, "A changed argument contract must be detected before sending a request.");
        });
        await CheckAction("thread-list-catalog-accepts-json-integer-limits", () =>
        {
            using var stream = new MemoryStream();
            using var bridge = new NativeBridge(stream, "fixture-caller");
            bridge.ReadToolCatalog(JsonNode.Parse("""
                {"tools":[{"namespace":"codex_app","name":"list_threads","inputSchema":
                {"type":"object","properties":{"limit":{"type":"integer"}},"additionalProperties":false}}]}
                """)!.AsObject());

            bridge.RequireArguments("list_threads", new JsonObject { ["limit"] = 50 });
            bridge.RequireArguments("list_threads", new JsonObject { ["limit"] = 50L });
            bridge.RequireArguments("list_threads", new JsonObject { ["limit"] = 50.0m });
            bridge.RequireArguments("list_threads", new JsonObject { ["limit"] = JsonNode.Parse("5e1") });
            foreach (JsonValue? invalid in new[] { JsonValue.Create(50.5m), JsonValue.Create("50") })
            {
                bool rejected = false;
                try { bridge.RequireArguments("list_threads", new JsonObject { ["limit"] = invalid }); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Reject fractional and string limits before sending a request.");
            }
        });
        await CheckAction("timestamp-variants-and-invalid-values", () =>
        {
            const long expected = 1_790_000_000_000;
            Require(SorterService.ReadTime(JsonValue.Create(expected)) == expected, "Millisecond timestamps.");
            Require(SorterService.ReadTime(JsonValue.Create(expected / 1000)) == expected, "Legacy timestamps in seconds.");
            Require(SorterService.ReadTime(JsonValue.Create(DateTimeOffset.FromUnixTimeMilliseconds(expected).ToString("O"))) == expected, "ISO timestamps.");
            Require(SorterService.ReadTime(JsonValue.Create("unknown")) == null && SorterService.ReadTime(JsonValue.Create(long.MaxValue)) == null,
                "Invalid timestamps sort as missing.");
        });
        await Check("automatic-watch-coalesces-writes-and-idles", () => VerifyAutomaticWatchAsync(Path.Combine(temporary, "automatic")));
        await Check("automatic-detects-returning-client-without-history-write", () => VerifyAutomaticReconnectAsync(Path.Combine(temporary, "reconnect")));
        await Check("automatic-stop-waits-for-current-operation", () => VerifyAutomaticStopAsync(Path.Combine(temporary, "automatic-stop")));
        await Check("automatic-rate-limit-deadline-survives-new-events", () => VerifyRateLimitBackoffAsync(Path.Combine(temporary, "automatic-limit")));
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

    private static async Task VerifyAutomaticWatchAsync(string root)
    {
        Directory.CreateDirectory(root);
        int calls = 0;
        await using var watcher = new AutomaticSorter(root, () => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        await WaitForAsync(() => Volatile.Read(ref calls) == 1);
        for (int index = 0; index < 8; index++)
            await File.WriteAllTextAsync(Path.Combine(root, ".codex-global-state.json"), index.ToString());
        await WaitForAsync(() => Volatile.Read(ref calls) == 2);
        await Task.Delay(1100);
        Require(calls == 2, "A burst requires one synchronization and idle watching requires none.");
    }

    private static async Task VerifyAutomaticStopAsync(string root)
    {
        Directory.CreateDirectory(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new AutomaticSorter(root, async () => { entered.SetResult(); await finish.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = watcher.DisposeAsync().AsTask();
        Require(!stopping.IsCompleted, "Stopping waits for the current native transaction.");
        finish.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task VerifyAutomaticReconnectAsync(string root)
    {
        int calls = 0;
        int connected = 0;
        await using var watcher = new AutomaticSorter(root, () =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, () => Volatile.Read(ref connected) == 1 ? ["reopened-client"] : [], TimeSpan.FromMilliseconds(50));
        await WaitForAsync(() => Volatile.Read(ref calls) == 1);
        Volatile.Write(ref connected, 1);
        await WaitForAsync(() => Volatile.Read(ref calls) == 2);
        await Task.Delay(200);
        Require(calls == 2, "An unchanged endpoint should not trigger repeated sorting.");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(30);
        Require(condition(), "Automatic synchronization timed out.");
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

    private static void VerifyHistory(string directory, int generation)
    {
        RuntimeHelpers.RunClassConstructor(typeof(LocalHistoryReader).TypeHandle);
        Directory.CreateDirectory(directory);
        var state = new JsonObject
        {
            ["thread-project-assignments"] = new JsonObject
            {
                ["parent"] = new JsonObject { ["projectKind"] = "local", ["projectId"] = "legacy-project" }
            },
            ["app-server-project-id-by-legacy-project-id-by-host"] = new JsonObject
            {
                ["local:" + Path.GetFullPath(directory)] = new JsonObject { ["legacy-project"] = "backend-project" }
            }
        };
        File.WriteAllText(Path.Combine(directory, ".codex-global-state.json"), state.ToJsonString());
        string databaseFile = Path.Combine(directory, "state_1.sqlite");
        Require(sqlite3_open(databaseFile, out IntPtr database, 0x10006, IntPtr.Zero) == 0, "Create fixture database.");
        try
        {
            var columns = new List<string> { "id TEXT", "title TEXT", "updated_at INTEGER", "archived INTEGER", "source TEXT" };
            if (generation >= 1) columns.Add("agent_path TEXT");
            if (generation >= 2) columns.Add("updated_at_ms INTEGER");
            if (generation >= 3) columns.Add("name TEXT");
            if (generation >= 4) columns.Add("project_id TEXT");
            if (generation >= 5) columns.Add("attached_thread_parent_id TEXT");
            string sql = "CREATE TABLE threads(" + string.Join(",", columns) + ");"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('parent','Parent',1,0,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('root','Root',2,0,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('archived','Archived',3,1,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('legacy-ms','Legacy milliseconds',1577836800123,0,'vscode');"
                + "INSERT INTO threads(id,title,updated_at,archived,source) VALUES('subagent','Subagent',4,0,'{\"subagent\":\"worker\"}');";
            if (generation >= 1)
                sql += "INSERT INTO threads(id,title,updated_at,archived,source,agent_path) VALUES('nested','Nested agent',4,0,'vscode','/root/worker');";
            if (generation >= 2)
                sql += "UPDATE threads SET updated_at_ms=2501 WHERE id='root';";
            if (generation >= 3)
                sql += "UPDATE threads SET name='Named root' WHERE id='root';"
                    + "UPDATE threads SET name='' WHERE id='parent';";
            if (generation >= 4)
                sql += "UPDATE threads SET project_id='backend-project' WHERE id='parent';";
            if (generation >= 5)
                sql += "UPDATE threads SET attached_thread_parent_id='' WHERE id='root';"
                    + "INSERT INTO threads(id,title,updated_at,archived,source,attached_thread_parent_id) VALUES('child','Child',4,0,'vscode','parent');";
            Require(sqlite3_exec(database, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error) == 0, Marshal.PtrToStringUTF8(error) ?? "Create fixture rows.");
        }
        finally { sqlite3_close(database); }
        string before = Convert.ToBase64String(File.ReadAllBytes(databaseFile));
        List<JsonObject> rows = LocalHistoryReader.Read(directory, new JsonObject { ["threads"] = new JsonArray(), ["pinnedThreads"] = new JsonArray() });
        Require(rows.Select(row => row["id"]!.GetValue<string>()).ToHashSet().SetEquals(["parent", "root", "legacy-ms"]), "Standalone active rows.");
        JsonObject root = rows.Single(row => row["id"]!.GetValue<string>() == "root");
        Require(root["title"]!.GetValue<string>() == (generation >= 3 ? "Named root" : "Root"), "Use a saved name when the schema provides it.");
        Require(root["updatedAt"]!.GetValue<long>() == (generation >= 2 ? 2501 : 2000), "Read available timestamp precision.");
        Require(rows.Single(row => row["id"]!.GetValue<string>() == "legacy-ms")["updatedAt"]!.GetValue<long>() == 1577836800123, "Preserve early millisecond values.");
        Require(rows.Single(row => row["id"]!.GetValue<string>() == "parent")["projectId"]!.GetValue<string>() == "legacy-project", "Resolve project membership across schema generations.");
        Require(Convert.ToBase64String(File.ReadAllBytes(databaseFile)) == before, "History bytes stay read-only.");
    }

    private static async Task VerifyRateLimitBackoffAsync(string root)
    {
        Directory.CreateDirectory(root);
        var limited = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        await using var sorter = new AutomaticSorter(root, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                return Task.FromException(new CodexRateLimitException(DateTimeOffset.UtcNow.AddSeconds(2)));
            recovered.TrySetResult(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        });
        sorter.Completed += error =>
        {
            if (error is CodexRateLimitException rateLimit) limited.TrySetResult(rateLimit.RetryAt);
        };
        DateTimeOffset until = await limited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sorter.Request();
        sorter.Request();
        await Task.Delay(1100);
        Require(Volatile.Read(ref attempts) == 1, "File changes must not retry before the cooldown expires.");
        Require(await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5)) >= until, "Automatic sorting resumes after the cooldown.");
    }

    private static async Task VerifyPipeAsync(bool rateLimited = false)
    {
        string pipeName = "sst-" + Guid.NewGuid().ToString("N")[..12];
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
        var reply = new JsonObject { ["id"] = parsed["id"]!.DeepClone(), ["jsonrpc"] = "2.0" };
        if (rateLimited)
            reply["error"] = new JsonObject
            {
                ["code"] = 429, ["message"] = "Rate limited", ["data"] = new JsonObject { ["retryAfter"] = 6 }
            };
        else
            reply["result"] = new JsonObject
            {
                ["success"] = true,
                ["contentItems"] = new JsonArray(new JsonObject { ["type"] = "inputText", ["text"] = "{\"projects\":[]}" })
            };
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        byte[] response = Encoding.UTF8.GetBytes(reply.ToJsonString());
        await server.WriteAsync(BitConverter.GetBytes(response.Length).AsMemory(0, 2));
        await server.WriteAsync(BitConverter.GetBytes(response.Length).AsMemory(2, 2));
        await server.WriteAsync(response.AsMemory(0, 3));
        await server.WriteAsync(response.AsMemory(3));
        if (!rateLimited)
        {
            Require((await call)["projects"]!.AsArray().Count == 0, "Split native response.");
            return;
        }
        DateTimeOffset retryAt;
        try { await call; throw new Exception("The server's rate limit must be reported."); }
        catch (CodexRateLimitException error)
        {
            retryAt = error.RetryAt;
            Require(retryAt >= sentAt.AddSeconds(6) && retryAt <= DateTimeOffset.UtcNow.AddSeconds(6),
                "Honor the server's suggested wait.");
        }
        using var unopened = new MemoryStream();
        using var nextConnection = new NativeBridge(unopened, "fixture-caller");
        nextConnection.Tools.Add("list_projects");
        try { await nextConnection.CallAsync("list_projects", new JsonObject()); throw new Exception("Cooldown must apply to a fresh connection."); }
        catch (CodexRateLimitException error)
        {
            Require(error.RetryAt == retryAt && unopened.Length == 0, "Return the original deadline before sending another request.");
        }
    }

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);
}
