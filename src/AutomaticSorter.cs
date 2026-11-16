using System.Threading.Channels;

namespace SidebarSorter;

internal sealed class AutomaticSorter : IAsyncDisposable
{
    private readonly string dataRoot;
    private readonly Func<Task> synchronize;
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true
    });
    private readonly CancellationTokenSource stopping = new();
    private readonly FileSystemWatcher parentWatcher;
    private readonly object watcherLock = new();
    private FileSystemWatcher? historyWatcher;
    private readonly Task worker;
    public event Action<Exception?>? Completed;

    public AutomaticSorter(string dataRoot, Func<Task> synchronize)
    {
        this.dataRoot = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar);
        this.synchronize = synchronize;
        string parent = Path.GetDirectoryName(this.dataRoot)!;
        parentWatcher = new FileSystemWatcher(parent, Path.GetFileName(this.dataRoot))
        {
            NotifyFilter = NotifyFilters.DirectoryName, EnableRaisingEvents = true
        };
        parentWatcher.Created += (_, _) => { WatchHistory(); Request(); };
        parentWatcher.Renamed += (_, _) => { WatchHistory(); Request(); };
        parentWatcher.Deleted += (_, _) => WatchHistory();
        WatchHistory();
        worker = RunAsync();
        Request();
    }

    public void Request() => requests.Writer.TryWrite(true);

    private void WatchHistory()
    {
        lock (watcherLock)
        {
            historyWatcher?.Dispose();
            historyWatcher = null;
            if (!Directory.Exists(dataRoot) || stopping.IsCancellationRequested) return;
            historyWatcher = new FileSystemWatcher(dataRoot)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            historyWatcher.Changed += HistoryChanged;
            historyWatcher.Created += HistoryChanged;
            historyWatcher.Deleted += HistoryChanged;
            historyWatcher.Renamed += HistoryChanged;
            historyWatcher.Error += (_, _) => Request();
            historyWatcher.EnableRaisingEvents = true;
        }
    }

    private void HistoryChanged(object sender, FileSystemEventArgs change)
    {
        string name = change.Name ?? "";
        if (name is ".codex-global-state.json" or "session_index.jsonl"
            || name.StartsWith("state_", StringComparison.Ordinal) && name.Contains(".sqlite", StringComparison.Ordinal)) Request();
    }

    private async Task RunAsync()
    {
        int connectionFailures = 0;
        try
        {
            while (await requests.Reader.WaitToReadAsync(stopping.Token))
            {
                await Task.Delay(800, stopping.Token);
                while (requests.Reader.TryRead(out _)) { }
                try { await synchronize(); connectionFailures = 0; Completed?.Invoke(null); }
                catch (Exception error)
                {
                    Completed?.Invoke(error);
                    if (error is IOException && ++connectionFailures <= 6)
                    {
                        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(1 << connectionFailures, 30));
                        await Task.WhenAny(Task.Delay(delay, stopping.Token), requests.Reader.WaitToReadAsync(stopping.Token).AsTask());
                        requests.Writer.TryWrite(true);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    public static async Task<int> SynchronizeByNameAsync(SorterService service)
    {
        List<SortScope> scopes = await service.RefreshAsync();
        List<SortRow> projects = await service.GetRowsAsync(scopes[0]);
        CatalogResult catalog = await service.ReadCatalogAsync();
        if (catalog.Problems.Count > 0) throw new IOException(string.Join("\n", catalog.Problems.Values));
        WorkspacePreview preview = WorkspacePreview.Create(service.Projects, projects, catalog);
        preview.SortProjects(0);
        preview.SortChats(0);
        List<ProjectGroup> changedGroups = preview.Groups.Where(group => group.IsLoaded && group.Scope.Source == "codex"
            && !group.Chats.Select(SorterService.RowKey).SequenceEqual(group.OriginalChats.Select(SorterService.RowKey))).ToList();
        bool projectsChanged = preview.Changes(true, []).Count > 0;
        bool modeChanged = service.Snapshot["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() != "manual";
        int writes = 0;
        if (projectsChanged || changedGroups.Count > 0 || modeChanged)
        {
            var plans = changedGroups.Select(group => new ProjectChatPlan(group.Scope, group.Chats.Where(row => !row.IsPinned).ToList())).ToList();
            await service.ApplyWorkspaceAsync(preview.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList(),
                projectsChanged, plans, false, applyChatMode: true);
            writes += (projectsChanged ? 1 : 0) + plans.Sum(plan => plan.Rows.Count) + (modeChanged ? 1 : 0);
        }
        foreach (SortScope scope in scopes.Where(scope => scope.Kind == "section"))
        {
            List<SortRow> rows = await service.GetRowsAsync(scope);
            List<string> previous = rows.Select(SorterService.RowKey).ToList();
            SorterService.Sort(rows, 0);
            bool orderChanged = !rows.Select(SorterService.RowKey).SequenceEqual(previous);
            bool pinnedModeChanged = scope.Id == "pinned" && service.Snapshot["sidebarPreferences"]?["sorting"]?["pinned"]?.GetValue<string>() != "manual";
            if (!orderChanged && !pinnedModeChanged) continue;
            await service.ApplyAsync(scope, rows, false);
            writes++;
        }
        return writes;
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        parentWatcher.Dispose();
        lock (watcherLock) { historyWatcher?.Dispose(); historyWatcher = null; }
        await worker;
        stopping.Dispose();
    }
}
