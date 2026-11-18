using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SidebarSorter;

internal static class LocalHistoryReader
{
    static LocalHistoryReader()
    {
        if (!OperatingSystem.IsWindows())
            NativeLibrary.SetDllImportResolver(typeof(LocalHistoryReader).Assembly, (name, assembly, searchPath) =>
                name == "winsqlite3.dll"
                    ? NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "libsqlite3.so.0")
                    : IntPtr.Zero);
    }

    private const int OpenReadOnly = 0x00000001;
    private const int OpenFullMutex = 0x00010000;
    private const int RowAvailable = 100;
    private const int QueryComplete = 101;

    public static List<JsonObject> Read(string dataRoot, JsonObject sidebar)
    {
        JsonObject state;
        using (var input = new FileStream(Path.Combine(dataRoot, ".codex-global-state.json"), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            try { state = JsonNode.Parse(input)!.AsObject(); }
            catch (JsonException error) { throw new InvalidDataException(Localization.Text("The local project index could not be read. Refresh and try again."), error); }
        }

        string databasePath = Directory.GetFiles(dataRoot, "state_*.sqlite")
            .Select(path => (Path: path, Match: Regex.Match(Path.GetFileName(path), @"^state_(\d+)\.sqlite$")))
            .Where(item => item.Match.Success)
            .OrderByDescending(item => int.Parse(item.Match.Groups[1].Value))
            .Select(item => item.Path).FirstOrDefault()
            ?? throw new IOException(Localization.Text("The local history index is pending. Open Codex and refresh."));

        var legacyByBackend = new Dictionary<string, string>();
        if (state["app-server-project-id-by-legacy-project-id-by-host"] is JsonObject mappings)
        {
            JsonObject? local = mappings.FirstOrDefault(pair => string.Equals(pair.Key,
                "local:" + Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
            if (local != null)
                foreach (var pair in local) legacyByBackend[pair.Value!.GetValue<string>()] = pair.Key;
        }
        JsonObject assignments = state["thread-project-assignments"] as JsonObject ?? new();
        JsonObject hosts = state["thread-project-membership-host-ids"] as JsonObject ?? new();
        JsonObject orders = state["sidebar-project-thread-orders"] as JsonObject ?? new();
        var positions = new Dictionary<string, int>();
        foreach (var project in orders)
        {
            if (project.Value?["threadIds"] is not JsonArray ids) continue;
            for (int index = 0; index < ids.Count; index++) positions.TryAdd(ids[index]!.GetValue<string>(), index);
        }

        List<JsonObject> metadata = ReadDatabase(databasePath, legacyByBackend, assignments, hosts);
        var currentRows = sidebar["threads"]!.AsArray().Concat(sidebar["pinnedThreads"]!.AsArray())
            .Where(item => item?["kind"]?.GetValue<string>() == "codex" && (item["hostId"]?.GetValue<string>() ?? "local") == "local")
            .Select(item => item!.AsObject()).GroupBy(item => item["id"]!.GetValue<string>())
            .ToDictionary(group => group.Key, group => group.First());
        var pinned = sidebar["pinnedThreads"]!.AsArray().Where(item => item?["kind"]?.GetValue<string>() == "codex"
            && (item["hostId"]?.GetValue<string>() ?? "local") == "local")
            .Select(item => item!["id"]!.GetValue<string>()).ToHashSet();
        foreach (JsonObject row in metadata)
        {
            string id = row["id"]!.GetValue<string>();
            if (currentRows.TryGetValue(id, out JsonObject? current))
            {
                row["status"] = current["status"]?.DeepClone();
                row["title"] = current["title"]?.DeepClone() ?? row["title"]!.DeepClone();
            }
            row["isPinned"] = pinned.Contains(id);
        }
        bool manual = sidebar["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() == "manual";
        return metadata.OrderBy(row => manual ? positions.GetValueOrDefault(row["id"]!.GetValue<string>(), int.MaxValue) : 0)
            .ThenByDescending(row => row["updatedAt"]?.GetValue<long>()).ToList();
    }

    private static List<JsonObject> ReadDatabase(string path, Dictionary<string, string> legacyByBackend,
        JsonObject assignments, JsonObject hosts)
    {
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            int opened = sqlite3_open_v2(path, out database, OpenReadOnly | OpenFullMutex, IntPtr.Zero);
            if (opened != 0) throw new IOException(Localization.Text("Opening history read-only failed. SQLite status ") + opened);
            if (sqlite3_db_readonly(database, "main") != 1) throw new InvalidOperationException(Localization.Text("The history connection requires read-only mode."));
            sqlite3_busy_timeout(database, 1500);
            bool HasColumn(string column) => sqlite3_table_column_metadata(database, "main", "threads", column,
                out _, out _, out _, out _, out _) == 0;
            string legacyTitle = HasColumn("title") ? "NULLIF(title,'')" : "NULL";
            string title = HasColumn("name") ? "COALESCE(NULLIF(name,'')," + legacyTitle + ")" : legacyTitle;
            // Match the upstream timestamp migration, which also handles early millisecond values.
            string legacyUpdated = HasColumn("updated_at")
                ? "CASE WHEN updated_at < 1577836800000 THEN updated_at*1000 ELSE updated_at END" : "NULL";
            string updated = HasColumn("updated_at_ms") ? "COALESCE(updated_at_ms," + legacyUpdated + ")" : legacyUpdated;
            string projectColumn = HasColumn("project_id") ? "project_id" : "NULL";
            string query = "SELECT id," + title + "," + updated + "," + projectColumn
                + " FROM threads WHERE archived=0 AND source NOT LIKE '{%'";
            if (HasColumn("agent_path"))
                query += " AND (agent_path IS NULL OR agent_path='/root')";
            if (HasColumn("attached_thread_parent_id"))
                query += " AND (attached_thread_parent_id IS NULL OR attached_thread_parent_id='')";
            if (sqlite3_prepare_v2(database, query, -1, out statement, IntPtr.Zero) != 0)
                throw new InvalidDataException(Localization.Text("This version of the history schema needs an updated adapter. ") + Marshal.PtrToStringUTF8(sqlite3_errmsg(database)));
            if (sqlite3_stmt_readonly(statement) != 1) throw new InvalidOperationException(Localization.Text("The history query requires a read-only statement."));

            var rows = new List<JsonObject>();
            int step;
            while ((step = sqlite3_step(statement)) == RowAvailable)
            {
                string id = ReadText(statement, 0)!;
                string? backend = ReadText(statement, 3);
                string? project = backend == null ? null : legacyByBackend.GetValueOrDefault(backend, backend);
                if (project == null && (hosts[id]?.GetValue<string>() ?? "local") == "local"
                    && assignments[id] is JsonObject assignment && assignment["projectKind"]?.GetValue<string>() == "local")
                    project = assignment["projectId"]?.GetValue<string>();
                rows.Add(new JsonObject
                {
                    ["id"] = id, ["title"] = ReadText(statement, 1) ?? Localization.Text("Untitled chat"),
                    ["updatedAt"] = sqlite3_column_type(statement, 2) == 5 ? null : sqlite3_column_int64(statement, 2),
                    ["projectId"] = project,
                    ["kind"] = "codex", ["hostId"] = "local", ["status"] = "notLoaded"
                });
            }
            if (step != QueryComplete) throw new IOException(Localization.Text("Reading history failed. SQLite status ") + step);
            return rows;
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (database != IntPtr.Zero) sqlite3_close(database);
        }
    }

    private static string? ReadText(IntPtr statement, int column) => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, column));

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_db_readonly(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_table_column_metadata(IntPtr database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string databaseName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string tableName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string columnName,
        out IntPtr dataType, out IntPtr collation, out int notNull, out int primaryKey, out int autoIncrement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_stmt_readonly(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
}
