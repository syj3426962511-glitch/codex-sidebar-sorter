using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal sealed record SortScope(string Kind, string Id, string Label, string HostId = "local", string Source = "codex")
{
    public override string ToString() => Kind switch
    {
        "projects" => Localization.Text("Project list"),
        "project" => Localization.Text("Project chats \u00b7 ") + Label,
        "section" when Id == "pinned" => Localization.Text("Pinned chats"),
        _ => Localization.Text("Section \u00b7 ") + Label
    };
}

internal sealed record SortRow(string Id, string Title, long? UpdatedAt = null, string Source = "codex", string HostId = "local", string Status = "unknown", bool IsPinned = false);
internal sealed record CatalogChat(SortRow Row, string? ProjectId);
internal sealed record CatalogResult(List<CatalogChat> Chats, Dictionary<string, string> Problems);
internal sealed record ProjectChatPlan(SortScope Scope, List<SortRow> Rows);

internal sealed class SorterService(CancellationToken cancellationToken = default)
{
    public JsonObject Snapshot { get; private set; } = new();
    public List<JsonObject> Projects { get; private set; } = [];
    public bool LocalHistoryReadOnly { get; private set; }
    private readonly Dictionary<string, List<SortRow>> histories = new();

    public async Task<List<SortScope>> RefreshAsync()
    {
        using var bridge = await NativeBridge.ConnectAsync(cancellationToken);
        await ReadSnapshotAsync(bridge);
        var scopes = new List<SortScope> { new("projects", "threads", Localization.Text("Project list")), new("section", "pinned", Localization.Text("Pinned chats")) };
        foreach (JsonNode? section in Snapshot["sections"]!.AsArray())
        {
            string id = section!["sectionId"]!.GetValue<string>();
            if (id is not ("threads" or "chats" or "pinned"))
                scopes.Add(new("section", id, section["name"]!.GetValue<string>()));
        }
        foreach (JsonObject project in Projects)
        {
            if (project["projectKind"]?.GetValue<string>() == "chatgpt") continue;
            scopes.Add(new("project", project["projectId"]!.GetValue<string>(),
                project["label"]!.GetValue<string>(), project["hostId"]?.GetValue<string>() ?? "local"));
        }
        return scopes;
    }

    private async Task ReadSnapshotAsync(NativeBridge bridge)
    {
        JsonObject snapshot = await bridge.CallAsync("list_threads", new JsonObject());
        JsonObject result = await bridge.CallAsync("list_projects", new JsonObject());
        if (snapshot["sections"] is not JsonArray || snapshot["threads"] is not JsonArray
            || snapshot["pinnedThreads"] is not JsonArray || result["projects"] is not JsonArray projectList)
            throw new InvalidDataException(Localization.Text("The sorting result from Codex could not be read."));

        List<JsonObject> projects = projectList.Select(item => item!.AsObject()).ToList();
        Snapshot = snapshot;
        Projects = projects;
        histories.Clear();
        historyMetadata.Clear();
        LocalHistoryReadOnly = false;
    }

    public async Task<List<SortRow>> GetRowsAsync(SortScope scope)
    {
        if (scope.Kind == "projects")
        {
            Dictionary<string, JsonObject> byId = Projects.ToDictionary(p => p["projectId"]!.GetValue<string>());
            return GetSectionKeys("threads").Where(key => key.Contains(":project:", StringComparison.Ordinal))
                .Select(key => key.Split(':')[^1]).Where(byId.ContainsKey)
                .Select(id => new SortRow(id, byId[id]["label"]!.GetValue<string>())).ToList();
        }
        if (scope.Kind == "project")
            return await GetHistoryAsync(scope.Source, scope.HostId, scope.Id);

        string[] keys = GetSectionKeys(scope.Id).Where(key => key.Contains(":thread:", StringComparison.Ordinal)).ToArray();
        var rows = new List<SortRow>();
        var available = Snapshot["pinnedThreads"]!.AsArray().Concat(Snapshot["threads"]!.AsArray())
            .Select(item => ReadRow(item!.AsObject())).GroupBy(RowKey)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (string key in keys)
        {
            string[] parts = key.Split(':');
            string id = parts[^1];
            string source = parts[0];
            string host = parts.Length >= 4 ? parts[^2] : "local";
            string identity = source + ":" + host + ":" + id;
            if (!available.TryGetValue(identity, out SortRow? row))
            {
                foreach (SortRow historyRow in await GetHistoryAsync(source, host)) available.TryAdd(RowKey(historyRow), historyRow);
                if (!available.TryGetValue(identity, out row)) throw new InvalidDataException(Localization.Text("Some chats are temporarily unavailable. Refresh the list."));
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task FillProjectDatesAsync(List<SortRow> rows)
    {
        var dates = new Dictionary<string, long>();
        foreach (string host in Projects.Select(p => p["hostId"]?.GetValue<string>() ?? "local").Distinct())
        {
            await GetHistoryAsync("codex", host);
            string cacheKey = "codex:" + host;
            foreach (JsonObject thread in historyMetadata[cacheKey])
            {
                string? projectId = thread["projectId"]?.GetValue<string>();
                long? date = ReadTime(thread["updatedAt"]);
                if (projectId != null && date != null && (!dates.TryGetValue(projectId, out long previous) || date > previous)) dates[projectId] = date.Value;
            }
        }
        for (int index = 0; index < rows.Count; index++)
            if (dates.TryGetValue(rows[index].Id, out long date)) rows[index] = rows[index] with { UpdatedAt = date };
    }

    private readonly Dictionary<string, List<JsonObject>> historyMetadata = new();

    public async Task<CatalogResult> ReadCatalogAsync()
    {
        var sources = Projects.Select(project => (
            Source: project["projectKind"]?.GetValue<string>() == "chatgpt" ? "chatgpt" : "codex",
            Host: project["hostId"]?.GetValue<string>() ?? "local")).ToList();
        sources.Add(("codex", "local"));
        foreach (JsonNode? item in Snapshot["pinnedThreads"]!.AsArray().Concat(Snapshot["threads"]!.AsArray()))
            sources.Add((item!["kind"]?.GetValue<string>() ?? "codex", item["hostId"]?.GetValue<string>() ?? "local"));

        var chats = new Dictionary<string, CatalogChat>(StringComparer.Ordinal);
        var problems = new Dictionary<string, string>();
        foreach ((string source, string host) in sources.Distinct())
        {
            string key = source + ":" + host;
            try
            {
                await GetHistoryAsync(source, host);
                foreach (JsonObject item in historyMetadata[key])
                {
                    SortRow row = ReadRow(item, source, host);
                    chats.TryAdd(RowKey(row), new CatalogChat(row, item["projectId"]?.GetValue<string>()));
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                problems[key] = error.Message;
            }
        }
        foreach (JsonNode? item in Snapshot["pinnedThreads"]!.AsArray())
        {
            SortRow row = ReadRow(item!.AsObject()) with { IsPinned = true };
            string key = RowKey(row);
            if (chats.TryGetValue(key, out CatalogChat? existing))
                chats[key] = existing with { Row = row with { UpdatedAt = row.UpdatedAt ?? existing.Row.UpdatedAt } };
            else chats.Add(key, new CatalogChat(row, item["projectId"]?.GetValue<string>()));
        }
        foreach (JsonNode? item in Snapshot["threads"]!.AsArray())
        {
            SortRow row = ReadRow(item!.AsObject());
            chats.TryAdd(RowKey(row), new CatalogChat(row, item["projectId"]?.GetValue<string>()));
        }
        return new CatalogResult(chats.Values.ToList(), problems);
    }

    public async Task ApplyWorkspaceAsync(List<SortRow> projectRows, bool applyProjects,
        List<ProjectChatPlan> chatPlans, bool recentMode, Action<int, int>? progress = null, bool applyChatMode = false)
    {
        if (chatPlans.Any(plan => plan.Scope.Source != "codex"))
            throw new InvalidOperationException(Localization.Text("Batch sorting supports chats within Codex projects."));
        using var bridge = await NativeBridge.ConnectAsync(cancellationToken);
        await ReadSnapshotAsync(bridge);
        if (applyProjects)
            RequireSameIds(projectRows, await GetRowsAsync(new("projects", "threads", "")));
        if (!recentMode)
            foreach (ProjectChatPlan plan in chatPlans)
                RequireSameIds(plan.Rows, (await GetRowsAsync(plan.Scope)).Where(row => !row.IsPinned));

        string mode = recentMode ? "updated_at" : "manual";
        bool modeChanged = (applyChatMode || chatPlans.Count > 0)
            && Snapshot["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() != mode;
        var operations = new List<WriteOperation>();
        if (applyProjects && projectRows.Count > 0) operations.Add(ReorderProjects(projectRows));
        if (modeChanged) operations.Add(SetSortMode("chats", mode));
        if (!recentMode)
            foreach (ProjectChatPlan plan in chatPlans) operations.AddRange(ProjectMoves(plan.Scope, plan.Rows));
        await SubmitAsync(bridge, operations, progress);
        if (modeChanged) RecordMode("chats", mode);
    }

    private async Task<List<SortRow>> GetHistoryAsync(string source, string host, string? projectId = null)
    {
        string cacheKey = source + ":" + host;
        if (!histories.TryGetValue(cacheKey, out List<SortRow>? rows))
        {
            var metadata = new List<JsonObject>();
            if (source == "codex" && host == "local")
            {
                metadata = await Task.Run(() => LocalHistoryReader.Read(NativeBridge.DataRoot, Snapshot), cancellationToken);
                LocalHistoryReadOnly = true;
            }
            else
            {
                using var bridge = await NativeBridge.ConnectAsync(cancellationToken);
                string? cursor = null;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var arguments = new JsonObject { ["mode"] = "history", ["source"] = source };
                    if (source == "codex") arguments["hostId"] = host;
                    if (cursor != null) arguments["cursor"] = cursor;
                    JsonObject page = await bridge.CallAsync("list_threads", arguments);
                    if (page["threads"] is not JsonArray threads)
                        throw new InvalidDataException(Localization.Text("The sorting result from Codex could not be read."));
                    metadata.AddRange(threads.Select(item => item!.AsObject()));
                    cursor = page["nextCursor"]?.GetValue<string>();
                    if (cursor != null && !visited.Add(cursor))
                        throw new InvalidDataException(Localization.Text("The sorting result from Codex could not be read."));
                } while (cursor != null);
            }
            rows = metadata.Select(item => ReadRow(item, source, host)).ToList();
            histories[cacheKey] = rows;
            historyMetadata[cacheKey] = metadata;
        }
        if (projectId == null) return rows.ToList();
        var ids = historyMetadata[cacheKey].Where(item => item["projectId"]?.GetValue<string>() == projectId)
            .Select(item => item["id"]!.GetValue<string>()).ToHashSet();
        return rows.Where(row => ids.Contains(row.Id)).ToList();
    }

    public async Task ApplyAsync(SortScope scope, List<SortRow> rows, bool recentMode)
    {
        using var bridge = await NativeBridge.ConnectAsync(cancellationToken);
        await ReadSnapshotAsync(bridge);
        List<SortRow> currentRows = await GetRowsAsync(scope);
        if (!recentMode || scope.Kind == "projects" || scope.Kind == "section" && scope.Id != "pinned")
            RequireSameIds(rows, scope.Kind == "project" ? currentRows.Where(row => !row.IsPinned) : currentRows);
        if (scope.Kind == "projects")
        {
            if (rows.Count == 0) return;
            WriteOperation reorder = ReorderProjects(rows);
            await bridge.CallAsync(reorder.Tool, reorder.Arguments);
            return;
        }
        if (scope.Kind == "section" && scope.Id != "pinned")
        {
            WriteOperation reorder = ReorderSection(scope, rows);
            await bridge.CallAsync(reorder.Tool, reorder.Arguments);
            return;
        }
        string preferenceKey = scope.Id == "pinned" ? "pinned" : "chats";
        string mode = recentMode ? "updated_at" : "manual";
        bool modeChanged = Snapshot["sidebarPreferences"]?["sorting"]?[preferenceKey]?.GetValue<string>() != mode;
        var operations = new List<WriteOperation>();
        if (modeChanged) operations.Add(SetSortMode(preferenceKey, mode));
        if (!recentMode && scope.Kind == "section" && rows.Count > 0) operations.Add(ReorderSection(scope, rows));
        if (!recentMode && scope.Kind == "project") operations.AddRange(ProjectMoves(scope, rows));
        await SubmitAsync(bridge, operations);
        if (modeChanged) RecordMode(preferenceKey, mode);
    }

    private static void RequireSameIds(IEnumerable<SortRow> proposed, IEnumerable<SortRow> current)
    {
        var ids = proposed.Select(RowKey).ToArray();
        var currentIds = current.Select(RowKey).ToHashSet(StringComparer.Ordinal);
        if (ids.Length != currentIds.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || !currentIds.SetEquals(ids))
            throw new InvalidOperationException(Localization.Text("Chat membership changed. Refresh the list and apply again."));
    }

    private sealed record WriteOperation(string Tool, JsonObject Arguments, SortScope? Scope = null, SortRow? Row = null);

    private static WriteOperation ReorderProjects(IEnumerable<SortRow> rows) =>
        new("reorder_sidebar_projects", new JsonObject { ["projectIds"] = StringArray(rows.Select(row => row.Id)) });

    private static WriteOperation ReorderSection(SortScope scope, IEnumerable<SortRow> rows) =>
        new("reorder_section", new JsonObject { ["sectionId"] = scope.Id, ["threadIds"] = StringArray(rows.Select(row => row.Id)) });

    private static WriteOperation SetSortMode(string key, string mode) =>
        new("update_sidebar_preferences", new JsonObject { ["sorting"] = new JsonObject { [key] = mode } });

    private void RecordMode(string key, string mode)
    {
        if (Snapshot["sidebarPreferences"]?["sorting"] is not JsonObject sorting) return;
        sorting[key] = mode;
        if (key == "chats") sorting["projects"] = mode;
    }

    private static IEnumerable<WriteOperation> ProjectMoves(SortScope scope, List<SortRow> rows)
    {
        string? before = null;
        foreach (SortRow row in rows.AsEnumerable().Reverse())
        {
            var arguments = new JsonObject { ["threadId"] = row.Id, ["projectId"] = scope.Id, ["hostId"] = scope.HostId };
            if (before != null) arguments["beforeThreadId"] = before;
            yield return new WriteOperation("move_thread_to_project", arguments, scope, row);
            before = row.Id;
        }
    }

    private static async Task SubmitAsync(NativeBridge bridge, List<WriteOperation> operations, Action<int, int>? progress = null)
    {
        foreach (WriteOperation operation in operations) bridge.RequireArguments(operation.Tool, operation.Arguments);
        int completed = 0;
        foreach (WriteOperation operation in operations)
        {
            try { await bridge.CallAsync(operation.Tool, operation.Arguments); }
            catch (InvalidOperationException error) when (operation.Row != null && error.Message == "The task is not available in the sidebar.")
            {
                throw new InvalidOperationException(Localization.Text("Expand the Codex project \u201c") + operation.Scope!.Label + Localization.Text("\u201d, then refresh and try again. Pending chat \u201c") + operation.Row.Title + "”.", error);
            }
            catch (InvalidOperationException error) when (operation.Row != null && error.Message == "The task's source project could not be safely determined.")
            {
                throw new InvalidOperationException(Localization.Text("Check chat membership in the Codex project \u201c") + operation.Scope!.Label + Localization.Text("\u201d, then refresh and try again. Pending chat \u201c (membership)") + operation.Row.Title + "”.", error);
            }
            progress?.Invoke(++completed, operations.Count);
        }
    }

    public string[] GetSectionKeys(string id)
    {
        JsonObject? section = Snapshot["sections"]!.AsArray().Select(item => item!.AsObject())
            .FirstOrDefault(item => item["sectionId"]?.GetValue<string>() == id);
        return section?["itemKeys"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
    }

    private static SortRow ReadRow(JsonObject item, string source = "codex", string host = "local") => new(item["id"]!.GetValue<string>(), item["title"]?.GetValue<string>() ?? Localization.Text("Untitled chat (history)"),
        ReadTime(item["updatedAt"]), item["kind"]?.GetValue<string>() ?? source, item["hostId"]?.GetValue<string>() ?? host,
        item["status"]?.GetValue<string>() ?? "unknown", item["isPinned"]?.GetValue<bool>() ?? false);

    public static string RowKey(SortRow row) => row.Source + ":" + row.HostId + ":" + row.Id;

    internal static long? ReadTime(JsonNode? value)
    {
        if (value is not JsonValue scalar) return null;
        if (scalar.TryGetValue(out string? text))
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
                return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date.ToUnixTimeMilliseconds() : null;
            return NormalizeTime(numeric);
        }
        if (scalar.TryGetValue(out long whole)) return NormalizeTime(whole);
        return scalar.TryGetValue(out double timestamp) ? NormalizeTime(timestamp) : null;
    }

    private static long? NormalizeTime(double timestamp)
    {
        double milliseconds = Math.Abs(timestamp) < 100_000_000_000 ? timestamp * 1000 : timestamp;
        return double.IsFinite(milliseconds) && milliseconds is >= -62_135_596_800_000 and <= 253_402_300_799_999
            ? (long)milliseconds : null;
    }

    public static JsonArray StringArray(IEnumerable<string> values) => new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string left, string right);

    private static int CompareNatural(string left, string right, CompareInfo comparer)
    {
        if (OperatingSystem.IsWindows()) return StrCmpLogicalW(left, right);
        return comparer.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
    }

    public static void Sort(List<SortRow> rows, int mode)
    {
        if (mode == 6) return;
        CompareInfo comparer = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;
        var ranked = rows.Select((row, index) => (row, index)).ToList();
        ranked.Sort((left, right) =>
        {
            int result;
            if (mode >= 4)
            {
                if (left.row.UpdatedAt == null || right.row.UpdatedAt == null)
                    result = left.row.UpdatedAt == null ? (right.row.UpdatedAt == null ? 0 : 1) : -1;
                else result = mode == 4 ? right.row.UpdatedAt.Value.CompareTo(left.row.UpdatedAt.Value) : left.row.UpdatedAt.Value.CompareTo(right.row.UpdatedAt.Value);
            }
            else
            {
                result = mode >= 2 ? CompareNatural(left.row.Title, right.row.Title, comparer) : comparer.Compare(left.row.Title, right.row.Title, CompareOptions.IgnoreCase);
                if (mode % 2 == 1) result = -Math.Sign(result);
            }
            return result != 0 ? result : left.index.CompareTo(right.index);
        });
        rows.Clear();
        rows.AddRange(ranked.Select(item => item.row));
    }
}
