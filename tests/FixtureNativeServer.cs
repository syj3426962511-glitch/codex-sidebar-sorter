using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal sealed class FixtureNativeServer : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> connections = [];
    private readonly object modelLock = new();
    private readonly Task listener;
    private readonly JsonObject globalState = new();
    private readonly JsonObject sorting = new() { ["projects"] = "manual", ["chats"] = "manual", ["pinned"] = "manual" };
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sidebar-cloud-fixture-" + Guid.NewGuid().ToString("N"));
    private readonly string pipeName = "codex-app-tools-fixture-" + Guid.NewGuid().ToString("N");
    private string? socketPath;
    public List<(string Id, string Name)> Projects { get; } = [];
    public Dictionary<string, List<SortRow>> Chats { get; } = new();
    private readonly List<string> projectOrder = [];
    private readonly Dictionary<string, List<string>> chatOrders = new();
    private readonly Dictionary<string, List<string>> sectionOrders = new();
    public int WriteCount { get; private set; }
    public string? UnavailableTool { get; set; }

    public FixtureNativeServer()
    {
        RuntimeHelpers.RunClassConstructor(typeof(LocalHistoryReader).TypeHandle);
        Directory.CreateDirectory(Root);
        var assignments = new JsonObject();
        var backendIds = new JsonObject();
        var orders = new JsonObject();
        string[] names = ["Zulu", "Project 10", "Project-20", "Alpha", "Project 2", "Project-3"];
        string[] chatNames = ["Zulu 20", "Alpha 10", "Alpha 2", "beta", "ALPHA", "Same name", "Same name", "Research-20", "Research-3", "Data-2005", "Data-2003", "A long chat title for cloud acceptance"];
        var sql = new StringBuilder("CREATE TABLE threads(id TEXT,name TEXT,title TEXT,updated_at_ms INTEGER,updated_at INTEGER,project_id TEXT,archived INTEGER,source TEXT,agent_path TEXT,attached_thread_parent_id TEXT);");
        for (int projectIndex = 0; projectIndex < 30; projectIndex++)
        {
            string id = Guid.NewGuid().ToString();
            string backendId = Guid.NewGuid().ToString();
            string name = projectIndex < names.Length ? names[projectIndex] : "Lab " + (30 - projectIndex);
            Projects.Add((id, name));
            projectOrder.Add(id);
            backendIds[id] = backendId;
            var rows = new List<SortRow>();
            for (int chatIndex = 0; chatIndex < 12; chatIndex++)
            {
                string threadId = Guid.NewGuid().ToString();
                string title = chatNames[(chatIndex + projectIndex) % chatNames.Length];
                long updated = 1_790_000_000_000 + projectIndex * 1000 + chatIndex;
                rows.Add(new SortRow(threadId, title, updated));
                assignments[threadId] = new JsonObject { ["projectId"] = id, ["projectKind"] = "local" };
                sql.Append("INSERT INTO threads VALUES('").Append(threadId).Append("','").Append(title.Replace("'", "''"))
                    .Append("','Fixture conversation',").Append(updated).Append(",1790000000,'").Append(backendId).Append("',0,'vscode',NULL,NULL);");
            }
            Chats[id] = rows;
            chatOrders[id] = rows.Select(row => row.Id).ToList();
            orders[id] = new JsonObject { ["threadIds"] = SorterService.StringArray(chatOrders[id]) };
        }
        string parentId = Chats[Projects[0].Id][0].Id;
        for (int index = 0; index < 3; index++)
            sql.Append("INSERT INTO threads VALUES('").Append(Guid.NewGuid()).Append("','Attached fixture child','Child',1790000000000,1790000000,'")
                .Append(backendIds[Projects[0].Id]!.GetValue<string>()).Append("',0,'vscode',NULL,'").Append(parentId).Append("');");
        Require(sqlite3_open_v2(Path.Combine(Root, "state_1.sqlite"), out IntPtr database, 0x10006, IntPtr.Zero) == 0, "Create fixture history.");
        try { Require(sqlite3_exec(database, sql.ToString(), IntPtr.Zero, IntPtr.Zero, out _) == 0, "Populate fixture history."); }
        finally { sqlite3_close(database); }
        globalState["thread-project-assignments"] = assignments;
        sectionOrders["pinned"] = Chats[Projects[0].Id].Take(3).Select(row => row.Id).ToList();
        sectionOrders["fixture-section"] = Chats[Projects[1].Id].Take(3).Select(row => row.Id).ToList();
        globalState["sidebar-project-thread-orders"] = orders;
        globalState["app-server-project-id-by-legacy-project-id-by-host"] = new JsonObject { ["local:" + Root] = backendIds };
        SaveState();
        File.WriteAllText(Path.Combine(Root, "session_index.jsonl"), new JsonObject { ["id"] = parentId, ["thread_name"] = "Cloud fixture" }.ToJsonString() + "\n");
        listener = ListenAsync();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private void SaveState() => File.WriteAllText(Path.Combine(Root, ".codex-global-state.json"), globalState.ToJsonString());

    private async Task ListenAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                string directory = OperatingSystem.IsMacOS() ? "/tmp/codex-app-tools" : "/tmp/codex-app-tools-" + geteuid();
                Directory.CreateDirectory(directory);
                socketPath = Path.Combine(directory, "fixture-" + Guid.NewGuid().ToString("N")[..12] + ".sock");
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Bind(new UnixDomainSocketEndPoint(socketPath));
                socket.Listen(20);
                while (!stopping.IsCancellationRequested)
                {
                    Socket connection = await socket.AcceptAsync(stopping.Token);
                    connections.Add(ServeAsync(new NetworkStream(connection, ownsSocket: true)));
                }
                return;
            }
            while (!stopping.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { await pipe.WaitForConnectionAsync(stopping.Token); }
                catch { pipe.Dispose(); throw; }
                connections.Add(ServeAsync(pipe));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ServeAsync(Stream pipe)
    {
        await using (pipe)
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    var header = new byte[4];
                    await pipe.ReadExactlyAsync(header, stopping.Token);
                    int length = BitConverter.ToInt32(header);
                    Require(length > 0 && length < 8 * 1024 * 1024, "Fixture frame length.");
                    var body = new byte[length];
                    await pipe.ReadExactlyAsync(body, stopping.Token);
                    JsonObject request = JsonNode.Parse(body)!.AsObject();
                    JsonObject result;
                    lock (modelLock) result = Handle(request);
                    byte[] response = Encoding.UTF8.GetBytes(new JsonObject
                    {
                        ["id"] = request["id"]!.DeepClone(), ["jsonrpc"] = "2.0", ["result"] = result
                    }.ToJsonString());
                    await pipe.WriteAsync(BitConverter.GetBytes(response.Length), stopping.Token);
                    await pipe.WriteAsync(response, stopping.Token);
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }

    private JsonObject Handle(JsonObject request)
    {
        string[] tools = ["list_projects", "list_threads", "reorder_sidebar_projects", "move_thread_to_project", "update_sidebar_preferences", "reorder_section"];
        if (request["method"]!.GetValue<string>() == "tools/list")
            return new JsonObject { ["tools"] = new JsonArray(tools.Where(name => name != UnavailableTool).Select(name => (JsonNode)new JsonObject { ["namespace"] = "codex_app", ["name"] = name }).ToArray()) };
        JsonObject parameters = request["params"]!.AsObject();
        string tool = parameters["tool"]!.GetValue<string>();
        JsonObject args = parameters["arguments"]!.AsObject();
        JsonObject value;
        switch (tool)
        {
            case "list_projects":
                value = new JsonObject { ["projects"] = new JsonArray(Projects.Select(project => (JsonNode)new JsonObject
                {
                    ["projectId"] = project.Id, ["projectKind"] = "local", ["label"] = project.Name,
                    ["path"] = Path.Combine(Root, project.Id), ["hostId"] = "local"
                }).ToArray()) };
                break;
            case "list_threads":
                value = new JsonObject
                {
                    ["threads"] = new JsonArray(Chats.Values.SelectMany(rows => rows).Take(50).Select(row => (JsonNode)new JsonObject
                    {
                        ["id"] = row.Id, ["title"] = row.Title, ["kind"] = "codex", ["hostId"] = "local", ["updatedAt"] = row.UpdatedAt, ["status"] = "completed"
                    }).ToArray()),
                    ["pinnedThreads"] = new JsonArray(sectionOrders["pinned"].Select(id =>
                    {
                        SortRow row = Chats.Values.SelectMany(rows => rows).Single(row => row.Id == id);
                        return (JsonNode)new JsonObject { ["id"] = id, ["title"] = row.Title, ["kind"] = "codex", ["hostId"] = "local", ["updatedAt"] = row.UpdatedAt, ["isPinned"] = true };
                    }).ToArray()),
                    ["sections"] = new JsonArray(
                        new JsonObject { ["sectionId"] = "pinned", ["name"] = "Pinned", ["itemKeys"] = SorterService.StringArray(sectionOrders["pinned"].Select(id => "codex:thread:local:" + id)) },
                        new JsonObject { ["sectionId"] = "threads", ["name"] = "Projects", ["itemKeys"] = SorterService.StringArray(projectOrder.Select(id => "codex:project:" + id)) },
                        new JsonObject { ["sectionId"] = "chats", ["name"] = "Chats", ["itemKeys"] = new JsonArray() },
                        new JsonObject { ["sectionId"] = "fixture-section", ["name"] = "Research", ["itemKeys"] = SorterService.StringArray(sectionOrders["fixture-section"].Select(id => "codex:thread:local:" + id)) }),
                    ["sidebarPreferences"] = new JsonObject { ["sorting"] = sorting.DeepClone() }
                };
                break;
            case "reorder_sidebar_projects":
                List<string> projects = args["projectIds"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
                Require(projects.Count == projectOrder.Count && projects.Distinct().Count() == projectOrder.Count && projects.All(projectOrder.Contains), "Project scope.");
                projectOrder.Clear(); projectOrder.AddRange(projects);
                WriteCount++;
                value = new JsonObject();
                break;
            case "update_sidebar_preferences":
                foreach (var pair in args["sorting"]!.AsObject()) sorting[pair.Key] = pair.Value!.DeepClone();
                if (args["sorting"]?["chats"] != null) sorting["projects"] = sorting["chats"]!.DeepClone();
                WriteCount++;
                value = new JsonObject();
                break;
            case "move_thread_to_project":
                string projectId = args["projectId"]!.GetValue<string>();
                string threadId = args["threadId"]!.GetValue<string>();
                Require(Chats[projectId].Any(row => row.Id == threadId), "Project ownership.");
                List<string> order = chatOrders[projectId];
                order.Remove(threadId);
                string? before = args["beforeThreadId"]?.GetValue<string>();
                if (before == null) order.Add(threadId);
                else { Require(order.Contains(before), "Anchor membership."); order.Insert(order.IndexOf(before), threadId); }
                globalState["sidebar-project-thread-orders"]![projectId]!["threadIds"] = SorterService.StringArray(order);
                SaveState();
                WriteCount++;
                value = new JsonObject();
                break;
            case "reorder_section":
                string sectionId = args["sectionId"]!.GetValue<string>();
                List<string> ids = args["threadIds"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
                Require(ids.Count == sectionOrders[sectionId].Count && ids.Distinct().Count() == ids.Count && ids.All(sectionOrders[sectionId].Contains), "Complete section membership.");
                sectionOrders[sectionId] = ids;
                WriteCount++;
                SaveState();
                value = new JsonObject();
                break;
            default:
                throw new InvalidOperationException("Unsupported fixture tool " + tool);
        }
        return new JsonObject { ["success"] = true, ["contentItems"] = new JsonArray(new JsonObject { ["type"] = "inputText", ["text"] = value.ToJsonString() }) };
    }

    public bool IsSortedByName()
    {
        lock (modelLock)
        {
            var projectNames = Projects.ToDictionary(project => project.Id, project => project.Name);
            List<SortRow> expectedProjects = projectOrder.Select(id => new SortRow(id, projectNames[id])).ToList();
            SorterService.Sort(expectedProjects, 0);
            if (!projectOrder.SequenceEqual(expectedProjects.Select(row => row.Id))) return false;
            foreach (var project in Chats)
            {
                string[] saved = chatOrders[project.Key].Where(id => !sectionOrders["pinned"].Contains(id)).ToArray();
                var rows = project.Value.ToDictionary(row => row.Id);
                List<SortRow> expected = saved.Select(id => rows[id]).ToList();
                SorterService.Sort(expected, 0);
                if (!saved.SequenceEqual(expected.Select(row => row.Id))) return false;
            }
            return true;
        }
    }

    public bool AreSectionsSortedByName()
    {
        lock (modelLock)
        {
            var rows = Chats.Values.SelectMany(rows => rows).ToDictionary(row => row.Id);
            foreach (List<string> order in sectionOrders.Values)
            {
                List<SortRow> expected = order.Select(id => rows[id]).ToList();
                SorterService.Sort(expected, 0);
                if (!order.SequenceEqual(expected.Select(row => row.Id))) return false;
            }
            return true;
        }
    }

    public void RenameProject(string id, string name)
    {
        lock (modelLock)
        {
            int index = Projects.FindIndex(project => project.Id == id);
            Projects[index] = (id, name);
            SaveState();
        }
    }

    public void RenameChat(string projectId, string id, string title)
    {
        lock (modelLock)
        {
            List<SortRow> rows = Chats[projectId];
            int index = rows.FindIndex(row => row.Id == id);
            rows[index] = rows[index] with { Title = title };
            ExecuteSql("UPDATE threads SET name='" + title.Replace("'", "''") + "' WHERE id='" + id + "';");
        }
    }

    public string AddProject(string title)
    {
        lock (modelLock)
        {
            string id = Guid.NewGuid().ToString();
            Projects.Add((id, title));
            projectOrder.Add(id);
            Chats[id] = [];
            chatOrders[id] = [];
            globalState["app-server-project-id-by-legacy-project-id-by-host"]!["local:" + Root]![id] = Guid.NewGuid().ToString();
            globalState["sidebar-project-thread-orders"]![id] = new JsonObject { ["threadIds"] = new JsonArray() };
            SaveState();
            return id;
        }
    }

    public string AddChat(string projectId, string title)
    {
        lock (modelLock)
        {
            string id = Guid.NewGuid().ToString();
            Chats[projectId].Add(new SortRow(id, title, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            chatOrders[projectId].Add(id);
            globalState["thread-project-assignments"]![id] = new JsonObject { ["projectId"] = projectId, ["projectKind"] = "local" };
            globalState["sidebar-project-thread-orders"]![projectId]!["threadIds"] = SorterService.StringArray(chatOrders[projectId]);
            string backend = globalState["app-server-project-id-by-legacy-project-id-by-host"]!["local:" + Root]![projectId]!.GetValue<string>();
            ExecuteSql("INSERT INTO threads VALUES('" + id + "','" + title.Replace("'", "''") + "','Fixture',1790000000000,1790000000,'" + backend + "',0,'vscode',NULL,NULL);");
            SaveState();
            return id;
        }
    }

    private void ExecuteSql(string sql)
    {
        Require(sqlite3_open_v2(Path.Combine(Root, "state_1.sqlite"), out IntPtr database, 0x10006, IntPtr.Zero) == 0, "Open fixture history.");
        try { Require(sqlite3_exec(database, sql, IntPtr.Zero, IntPtr.Zero, out _) == 0, "Update fixture history."); }
        finally { sqlite3_close(database); }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.GetAwaiter().GetResult();
        Task.WhenAll(connections).GetAwaiter().GetResult();
        if (socketPath != null) File.Delete(socketPath);
        stopping.Dispose();
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);
    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint geteuid();
}
