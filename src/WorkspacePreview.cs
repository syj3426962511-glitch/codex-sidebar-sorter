using System.Text.Json.Nodes;

namespace SidebarSorter;

internal sealed class ProjectGroup
{
    public required SortScope Scope { get; init; }
    public required SortRow Row { get; init; }
    public required List<SortRow> Chats { get; set; }
    public required List<SortRow> OriginalChats { get; set; }
    public required Dictionary<string, int> OriginalPositions { get; set; }
    public bool CanReorder { get; init; }
    public bool IsLoaded { get; init; }
}

internal sealed record PreviewChange(string Title, int Before, int After, string Kind);
internal sealed record TreeEntry(ProjectGroup? Group, SortRow Row, bool IsProject, int Position, int OriginalPosition, bool IsLoose = false, int? ChildCount = null)
{
    public string Key => IsProject ? "project:" + Row.Id : SorterService.RowKey(Row);
}

internal sealed class WorkspacePreview
{
    public List<ProjectGroup> Groups { get; private set; } = [];
    public List<SortRow> LooseChats { get; private set; } = [];
    public Dictionary<string, string> Problems { get; private set; } = new();
    private List<string> originalProjectIds = [];
    private List<SortRow> originalLooseChats = [];

    public int ChatCount => Groups.Sum(group => group.Chats.Count) + LooseChats.Count;

    public static WorkspacePreview Create(List<JsonObject> projects, List<SortRow> projectOrder, CatalogResult catalog)
    {
        var result = new WorkspacePreview { Problems = catalog.Problems };
        var order = projectOrder.Select(row => row.Id).ToList();
        var projectMap = projects.ToDictionary(project => project["projectId"]!.GetValue<string>());
        var allIds = order.Concat(projectMap.Keys.Except(order)).ToList();
        var chatsByProject = catalog.Chats.Where(chat => chat.ProjectId != null)
            .GroupBy(chat => chat.ProjectId!).ToDictionary(group => group.Key, group => group.Select(chat => chat.Row).ToList());
        foreach (string id in allIds)
        {
            JsonObject project = projectMap[id];
            string source = project["projectKind"]?.GetValue<string>() == "chatgpt" ? "chatgpt" : "codex";
            string host = project["hostId"]?.GetValue<string>() ?? "local";
            List<SortRow> chats = chatsByProject.TryGetValue(id, out var rows) ? rows : [];
            long? updated = chats.Where(row => row.UpdatedAt != null).Select(row => row.UpdatedAt).Max();
            string title = project["label"]!.GetValue<string>();
            result.Groups.Add(new ProjectGroup
            {
                Scope = new SortScope("project", id, title, host, source),
                Row = new SortRow(id, title, updated, source, host),
                Chats = chats.ToList(), OriginalChats = chats.ToList(),
                OriginalPositions = chats.Select((row, index) => (Key: SorterService.RowKey(row), Position: index + 1)).ToDictionary(item => item.Key, item => item.Position),
                CanReorder = order.Contains(id), IsLoaded = !catalog.Problems.ContainsKey(source + ":" + host)
            });
        }
        result.LooseChats = catalog.Chats.Where(chat => chat.ProjectId == null || !projectMap.ContainsKey(chat.ProjectId))
            .Select(chat => chat.Row).ToList();
        result.originalProjectIds = allIds;
        result.originalLooseChats = result.LooseChats.ToList();
        return result;
    }

    public void SortProjects(int mode)
    {
        var movable = Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList();
        SorterService.Sort(movable, mode);
        var byId = Groups.ToDictionary(group => group.Scope.Id);
        int next = 0;
        Groups = Groups.Select(group => group.CanReorder ? byId[movable[next++].Id] : group).ToList();
    }

    public void SortChats(int mode)
    {
        foreach (ProjectGroup group in Groups.Where(group => group.IsLoaded && group.Scope.Source == "codex"))
            SortGroupChats(group, mode);
    }

    public static void SortGroupChats(ProjectGroup group, int mode)
    {
        List<SortRow> movable = group.Chats.Where(row => !row.IsPinned).ToList();
        SorterService.Sort(movable, mode);
        ReplaceMovable(group.Chats, movable, row => !row.IsPinned);
    }

    public int OriginalProjectPosition(ProjectGroup group) => originalProjectIds.IndexOf(group.Scope.Id) + 1;
    public static int OriginalChatPosition(ProjectGroup group, SortRow row) => group.OriginalPositions.GetValueOrDefault(SorterService.RowKey(row));

    public List<PreviewChange> Changes(bool includeProjects, IEnumerable<ProjectGroup> chatGroups)
    {
        var changes = new List<PreviewChange>();
        if (includeProjects)
        {
            for (int index = 0; index < Groups.Count; index++)
            {
                ProjectGroup group = Groups[index];
                int before = OriginalProjectPosition(group);
                if (group.CanReorder && before != index + 1)
                    changes.Add(new PreviewChange(group.Row.Title, before, index + 1, Localization.Text("Projects")));
            }
        }
        foreach (ProjectGroup group in chatGroups)
        {
            for (int index = 0; index < group.Chats.Count; index++)
            {
                SortRow row = group.Chats[index];
                int before = OriginalChatPosition(group, row);
                if (before != index + 1) changes.Add(new PreviewChange(row.Title, before, index + 1, Localization.Text("Chats")));
            }
        }
        return changes;
    }

    public bool MoveTo(TreeEntry source, TreeEntry target)
    {
        if (source.IsProject && target.IsProject && source.Group?.CanReorder == true && target.Group?.CanReorder == true)
        {
            List<ProjectGroup> movable = Groups.Where(group => group.CanReorder).ToList();
            int from = movable.FindIndex(group => group.Scope.Id == source.Row.Id);
            int to = movable.FindIndex(group => group.Scope.Id == target.Row.Id);
            Move(movable, from, to);
            ReplaceMovable(Groups, movable, group => group.CanReorder);
            return from != to;
        }
        if (!source.IsProject && !target.IsProject && source.Group != null && source.Group == target.Group)
        {
            if (source.Row.IsPinned || target.Row.IsPinned) return false;
            List<SortRow> rows = source.Group.Chats.Where(row => !row.IsPinned).ToList();
            int from = rows.FindIndex(row => SorterService.RowKey(row) == source.Key);
            int to = rows.FindIndex(row => SorterService.RowKey(row) == target.Key);
            Move(rows, from, to);
            ReplaceMovable(source.Group.Chats, rows, row => !row.IsPinned);
            return from != to;
        }
        return false;
    }

    private static void Move<T>(List<T> values, int from, int to)
    {
        if (from < 0 || to < 0 || from == to) return;
        T item = values[from];
        values.RemoveAt(from);
        values.Insert(to, item);
    }

    private static void ReplaceMovable<T>(List<T> values, List<T> movable, Func<T, bool> canMove)
    {
        int next = 0;
        for (int index = 0; index < values.Count; index++)
            if (canMove(values[index])) values[index] = movable[next++];
    }

    public void Restore()
    {
        var byId = Groups.ToDictionary(group => group.Scope.Id);
        Groups = originalProjectIds.Select(id => byId[id]).ToList();
        foreach (ProjectGroup group in Groups) group.Chats = group.OriginalChats.ToList();
        LooseChats = originalLooseChats.ToList();
    }

    public void Accept(bool projects, IEnumerable<ProjectGroup> chats)
    {
        if (projects) originalProjectIds = Groups.Select(group => group.Scope.Id).ToList();
        foreach (ProjectGroup group in chats)
        {
            group.OriginalChats = group.Chats.ToList();
            group.OriginalPositions = group.Chats.Select((row, index) => (Key: SorterService.RowKey(row), Position: index + 1)).ToDictionary(item => item.Key, item => item.Position);
        }
    }
}
