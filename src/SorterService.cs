using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal sealed record SortScope(string Kind, string Id, string Label, string HostId = "local", string Source = "codex")
{
    public override string ToString() => Kind switch
    {
        "projects" => Localization.Text("项目列表"),
        "project" => Localization.Text("项目内聊天 · ") + Label,
        "section" when Id == "pinned" => Localization.Text("置顶聊天"),
        _ => Localization.Text("分区 · ") + Label
    };
}

internal sealed record SortRow(string Id, string Title, long? UpdatedAt = null, string Source = "codex", string HostId = "local", string Status = "unknown", bool IsPinned = false);
internal sealed record CatalogChat(SortRow Row, string? ProjectId);
internal sealed record CatalogResult(List<CatalogChat> Chats, Dictionary<string, string> Problems);
internal sealed record ProjectChatPlan(SortScope Scope, List<SortRow> Rows);

internal sealed class SorterService
{
    public JsonObject Snapshot { get; private set; } = new();
    public List<JsonObject> Projects { get; private set; } = [];
    public bool LocalHistoryReadOnly { get; private set; }
    private readonly Dictionary<string, List<SortRow>> histories = new();

    public async Task<List<SortScope>> RefreshAsync()
    {
        using var bridge = await NativeBridge.ConnectAsync();
        Snapshot = await bridge.CallAsync("list_threads", new JsonObject { ["limit"] = 50 });
        JsonObject result = await bridge.CallAsync("list_projects", new JsonObject());
        Projects = result["projects"]!.AsArray().Select(item => item!.AsObject()).ToList();
        histories.Clear();
        historyMetadata.Clear();
        LocalHistoryReadOnly = false;
        var scopes = new List<SortScope> { new("projects", "threads", Localization.Text("项目列表")), new("section", "pinned", Localization.Text("置顶聊天")) };
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
            .Select(item => item!.AsObject()).GroupBy(item => item["id"]!.GetValue<string>())
            .ToDictionary(group => group.Key, group => ReadRow(group.First()));
        foreach (string key in keys)
        {
            string[] parts = key.Split(':');
            string id = parts[^1];
            if (!available.TryGetValue(id, out SortRow? row))
            {
                string source = parts[0];
                string host = parts.Length >= 4 ? parts[^2] : "local";
                foreach (SortRow historyRow in await GetHistoryAsync(source, host)) available[historyRow.Id] = historyRow;
                if (!available.TryGetValue(id, out row)) throw new InvalidDataException(Localization.Text("部分聊天暂时不可读取，请刷新列表。"));
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

        var chats = new List<CatalogChat>();
        var problems = new Dictionary<string, string>();
        foreach ((string source, string host) in sources.Distinct())
        {
            string key = source + ":" + host;
            try
            {
                await GetHistoryAsync(source, host);
                chats.AddRange(historyMetadata[key].Select(item => new CatalogChat(ReadRow(item), item["projectId"]?.GetValue<string>())));
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
            {
                problems[key] = error.Message;
            }
        }
        foreach (JsonNode? item in Snapshot["pinnedThreads"]!.AsArray())
        {
            SortRow row = ReadRow(item!.AsObject()) with { IsPinned = true };
            int existing = chats.FindIndex(chat => RowKey(chat.Row) == RowKey(row));
            if (existing >= 0) chats[existing] = chats[existing] with { Row = row };
            else chats.Add(new CatalogChat(row, item["projectId"]?.GetValue<string>()));
        }
        foreach (JsonNode? item in Snapshot["threads"]!.AsArray())
        {
            SortRow row = ReadRow(item!.AsObject());
            if (chats.All(chat => RowKey(chat.Row) != RowKey(row)))
                chats.Add(new CatalogChat(row, item["projectId"]?.GetValue<string>()));
        }
        return new CatalogResult(chats.DistinctBy(chat => RowKey(chat.Row)).ToList(), problems);
    }

    public async Task ApplyWorkspaceAsync(List<SortRow> projectRows, bool applyProjects,
        List<ProjectChatPlan> chatPlans, bool recentMode, Action<int, int>? progress = null, bool applyChatMode = false)
    {
        if (chatPlans.Any(plan => plan.Scope.Source != "codex"))
            throw new InvalidOperationException(Localization.Text("当前批量重排范围支持 Codex 项目内对话。"));
        if (!recentMode)
        {
            foreach (var source in chatPlans.Select(plan => (plan.Scope.Source, plan.Scope.HostId)).Distinct())
            {
                string key = source.Source + ":" + source.HostId;
                histories.Remove(key);
                await GetHistoryAsync(source.Source, source.HostId);
                var memberships = historyMetadata[key].ToDictionary(item => item["id"]!.GetValue<string>(), item => item["projectId"]?.GetValue<string>());
                foreach (ProjectChatPlan plan in chatPlans.Where(plan => plan.Scope.Source == source.Source && plan.Scope.HostId == source.HostId))
                    if (plan.Rows.Any(row => !memberships.TryGetValue(row.Id, out string? currentProject) || currentProject != plan.Scope.Id))
                        throw new InvalidOperationException(Localization.Text("对话归属已变化，请刷新列表后重新应用。"));
            }
        }
        using var bridge = await NativeBridge.ConnectAsync();
        string mode = recentMode ? "updated_at" : "manual";
        bool modeChanged = (applyChatMode || chatPlans.Count > 0)
            && Snapshot["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() != mode;
        int total = (applyProjects && projectRows.Count > 0 ? 1 : 0) + (modeChanged ? 1 : 0)
            + (recentMode ? 0 : chatPlans.Sum(plan => plan.Rows.Count));
        int completed = 0;
        if (applyProjects && projectRows.Count > 0)
        {
            await bridge.CallAsync("reorder_sidebar_projects", new JsonObject { ["projectIds"] = StringArray(projectRows.Select(row => row.Id)) });
            progress?.Invoke(++completed, total);
        }
        if (modeChanged)
        {
            await bridge.CallAsync("update_sidebar_preferences", new JsonObject { ["sorting"] = new JsonObject { ["chats"] = mode } });
            Snapshot["sidebarPreferences"]!["sorting"]!["chats"] = mode;
            Snapshot["sidebarPreferences"]!["sorting"]!["projects"] = mode;
            progress?.Invoke(++completed, total);
        }
        if (recentMode) return;
        foreach (ProjectChatPlan plan in chatPlans)
            await MoveProjectRowsAsync(bridge, plan.Scope, plan.Rows, () => progress?.Invoke(++completed, total));
    }

    private async Task<List<SortRow>> GetHistoryAsync(string source, string host, string? projectId = null)
    {
        string cacheKey = source + ":" + host;
        if (!histories.TryGetValue(cacheKey, out List<SortRow>? rows))
        {
            var metadata = new List<JsonObject>();
            if (source == "codex" && host == "local")
            {
                metadata = await Task.Run(() => LocalHistoryReader.Read(NativeBridge.DataRoot, Snapshot));
                LocalHistoryReadOnly = true;
            }
            else
            {
                using var bridge = await NativeBridge.ConnectAsync();
                string? cursor = null;
                do
                {
                    var arguments = new JsonObject { ["mode"] = "history", ["source"] = source, ["limit"] = 50 };
                    if (source == "codex") arguments["hostId"] = host;
                    if (cursor != null) arguments["cursor"] = cursor;
                    JsonObject page = await bridge.CallAsync("list_threads", arguments);
                    metadata.AddRange(page["threads"]!.AsArray().Select(item => item!.AsObject()));
                    cursor = page["nextCursor"]?.GetValue<string>();
                } while (cursor != null);
            }
            rows = metadata.Select(ReadRow).ToList();
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
        if (scope.Kind == "project" && !recentMode)
        {
            histories.Remove(scope.Source + ":" + scope.HostId);
            HashSet<string> currentIds = (await GetHistoryAsync(scope.Source, scope.HostId, scope.Id))
                .Select(row => row.Id).ToHashSet();
            if (rows.Any(row => !currentIds.Contains(row.Id)))
                throw new InvalidOperationException(Localization.Text("聊天归属已经变化，请刷新列表后重新应用。"));
        }
        using var bridge = await NativeBridge.ConnectAsync();
        if (scope.Kind == "projects")
        {
            if (rows.Count == 0) return;
            await bridge.CallAsync("reorder_sidebar_projects", new JsonObject { ["projectIds"] = StringArray(rows.Select(row => row.Id)) });
            return;
        }
        if (scope.Kind == "section" && scope.Id != "pinned")
        {
            await bridge.CallAsync("reorder_section", new JsonObject
            {
                ["sectionId"] = scope.Id, ["threadIds"] = StringArray(rows.Select(row => row.Id))
            });
            return;
        }
        string preferenceKey = scope.Id == "pinned" ? "pinned" : "chats";
        await bridge.CallAsync("update_sidebar_preferences", new JsonObject
        {
            ["sorting"] = new JsonObject { [preferenceKey] = recentMode ? "updated_at" : "manual" }
        });
        Snapshot["sidebarPreferences"]!["sorting"]![preferenceKey] = recentMode ? "updated_at" : "manual";
        if (preferenceKey == "chats") Snapshot["sidebarPreferences"]!["sorting"]!["projects"] = recentMode ? "updated_at" : "manual";
        if (recentMode) return;
        if (scope.Kind == "section")
        {
            await bridge.CallAsync("reorder_section", new JsonObject
            {
                ["sectionId"] = scope.Id, ["threadIds"] = StringArray(rows.Select(row => row.Id))
            });
            return;
        }
        await MoveProjectRowsAsync(bridge, scope, rows);
    }

    private static async Task MoveProjectRowsAsync(NativeBridge bridge, SortScope scope, List<SortRow> rows, Action? progress = null)
    {
        string? before = null;
        foreach (SortRow row in rows.AsEnumerable().Reverse())
        {
            var arguments = new JsonObject { ["threadId"] = row.Id, ["projectId"] = scope.Id, ["hostId"] = scope.HostId };
            if (before != null) arguments["beforeThreadId"] = before;
            try { await bridge.CallAsync("move_thread_to_project", arguments); }
            catch (InvalidOperationException error) when (error.Message == "The task is not available in the sidebar.")
            {
                throw new InvalidOperationException(Localization.Text("请在 Codex 展开“") + scope.Label + Localization.Text("”后刷新重试。待同步条目“") + row.Title + "”。", error);
            }
            catch (InvalidOperationException error) when (error.Message == "The task's source project could not be safely determined.")
            {
                throw new InvalidOperationException(Localization.Text("请在 Codex 侧栏确认“") + scope.Label + Localization.Text("”的对话归属后刷新重试。待同步条目“") + row.Title + "”。", error);
            }
            before = row.Id;
            progress?.Invoke();
        }
    }

    public string[] GetSectionKeys(string id)
    {
        JsonObject? section = Snapshot["sections"]!.AsArray().Select(item => item!.AsObject())
            .FirstOrDefault(item => item["sectionId"]?.GetValue<string>() == id);
        return section?["itemKeys"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
    }

    private static SortRow ReadRow(JsonObject item) => new(item["id"]!.GetValue<string>(), item["title"]?.GetValue<string>() ?? Localization.Text("未命名聊天"),
        ReadTime(item["updatedAt"]), item["kind"]?.GetValue<string>() ?? "codex", item["hostId"]?.GetValue<string>() ?? "local",
        item["status"]?.GetValue<string>() ?? "unknown", item["isPinned"]?.GetValue<bool>() ?? false);

    public static string RowKey(SortRow row) => row.Source + ":" + row.HostId + ":" + row.Id;

    private static long? ReadTime(JsonNode? value)
    {
        if (value == null) return null;
        double timestamp;
        if (value is JsonValue scalar && scalar.TryGetValue<long>(out long whole)) timestamp = whole;
        else timestamp = value.GetValue<double>();
        return checked((long)(timestamp < 1_000_000_000_000 ? timestamp * 1000 : timestamp));
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
