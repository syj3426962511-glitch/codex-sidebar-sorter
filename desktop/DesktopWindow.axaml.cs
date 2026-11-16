using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SidebarSorter;

internal sealed record SidebarDisplayRow(TreeEntry Entry, string Marker, string Title, string Detail, string Updated, Thickness Indent)
{
    public IBrush Background => Entry.IsProject ? new SolidColorBrush(Color.Parse("#F6F8FD")) : Brushes.White;
    public FontWeight Weight => Entry.IsProject ? FontWeight.Bold : FontWeight.Normal;
}

internal sealed partial class DesktopWindow : Window
{
    private static readonly string[] ModeKeys = ["名称升序", "名称降序", "数字自然升序", "数字自然降序", "最近更新优先", "最早更新优先", "手动顺序"];
    private readonly DesktopApp app;
    private readonly SorterService service = new();
    private readonly HashSet<string> expanded = new();
    private WorkspacePreview? workspace;
    private List<SortScope> scopes = [];
    private List<SortRow> sectionRows = [];
    private List<SortRow> originalSectionRows = [];
    private bool updating;
    private bool busy;
    private int tab;
    private SortScope? Scope => Box("ScopeBox").SelectedItem as SortScope;
    private ComboBox Box(string name) => this.FindControl<ComboBox>(name)!;
    private Button Button(string name) => this.FindControl<Button>(name)!;
    private CheckBox Check(string name) => this.FindControl<CheckBox>(name)!;
    private TextBlock Label(string name) => this.FindControl<TextBlock>(name)!;
    private ListBox RowList => this.FindControl<ListBox>("Rows")!;
    private int ProjectMode => Math.Max(0, Box("ProjectModeBox").SelectedIndex);
    private int ChatMode => Math.Max(0, Box("ChatModeBox").SelectedIndex);

    public DesktopWindow(DesktopApp app)
    {
        this.app = app;
        AvaloniaXamlLoader.Load(this);
        SetCaptions();
        Box("LanguageBox").SelectionChanged += (_, _) =>
        {
            if (updating || Box("LanguageBox").SelectedItem is not LanguageChoice choice) return;
            app.SaveSettings(app.Settings with { Language = choice.Code });
            SetCaptions();
            Render();
        };
        Box("ScopeBox").SelectionChanged += async (_, _) => { if (!updating) await RunAsync(LoadScopeAsync); };
        Box("ProjectModeBox").SelectionChanged += (_, _) => { if (!updating) ChangeModes(); };
        Box("ChatModeBox").SelectionChanged += (_, _) => { if (!updating) ChangeModes(); };
        Check("AutomaticBox").IsCheckedChanged += (_, _) =>
        {
            if (updating) return;
            bool enabled = Check("AutomaticBox").IsChecked == true;
            app.SaveSettings(app.Settings with { AutomaticNameSorting = enabled, ProjectSortMode = enabled ? 0 : ProjectMode, ChatSortMode = enabled ? 0 : ChatMode });
            if (enabled)
            {
                updating = true;
                Box("ProjectModeBox").SelectedIndex = 0;
                Box("ChatModeBox").SelectedIndex = 0;
                updating = false;
                SortPreview();
            }
            SetStatus(Localization.Text(enabled ? "自动保持名称升序" : "自动排序已暂停"));
        };
        Check("ApplyChatsBox").IsCheckedChanged += (_, _) => { if (!updating) RenderChecks(); };
        this.FindControl<TextBox>("Search")!.TextChanged += (_, _) => Render();
        Button("RefreshButton").Click += async (_, _) => await RefreshAsync();
        Button("ProjectsTab").Click += (_, _) => { tab = 0; Render(); };
        Button("ChatsTab").Click += (_, _) => { tab = 1; Render(); };
        Button("AllTab").Click += (_, _) => { tab = 2; Render(); };
        Button("UpButton").Click += (_, _) => MoveSelected(-1, false);
        Button("DownButton").Click += (_, _) => MoveSelected(1, false);
        Button("TopButton").Click += (_, _) => MoveSelected(-1, true);
        Button("BottomButton").Click += (_, _) => MoveSelected(1, true);
        Button("HideButton").Click += (_, _) => Close();
        Button("RestoreButton").Click += (_, _) => Restore();
        Button("ApplyButton").Click += async (_, _) => await RunAsync(ApplyAsync);
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            string[] args = DesktopProgram.Arguments;
            if (args.Length == 2 && args[0] == "--capture")
            {
                SaveScreenshot(args[1]);
                await app.ExitAsync();
            }
        };
    }

    private void SetCaptions()
    {
        updating = true;
        Title = Localization.Text("聊天排序工具");
        foreach (var (name, text) in new (string, string)[]
        {
            ("Heading","聊天排序"),("LanguageLabel","语言"),("ScopeLabel","范围"),("ListTitle","项目与项目内对话"),
            ("NameColumn","名称"),("DetailColumn","对话 / 状态"),("TimeColumn","最近更新"),("SortHeading","排序与变更"),
            ("ProjectModeLabel","第一层 · 项目"),("ChatModeLabel","第二层 · 项目内对话"),
            ("SharedModeLabel","项目内对话与最近聊天共用排序模式"),("CheckHeading","提交前检查")
        }) Label(name).Text = Localization.Text(text);
        foreach (var (name, text) in new (string, string)[]
        {
            ("RefreshButton","刷新"),("UpButton","↑ 上移"),("DownButton","↓ 下移"),("TopButton","移至顶部"),
            ("BottomButton","移至底部"),("RestoreButton","还原预览"),("HideButton","收起"),("ApplyButton","应用到侧栏")
        }) Button(name).Content = Localization.Text(text);
        Check("AutomaticBox").Content = Localization.Text("自动保持名称升序");
        Check("AutomaticBox").IsChecked = app.Settings.AutomaticNameSorting;
        Check("ApplyChatsBox").Content = Localization.Text("同时应用项目内对话排序");
        this.FindControl<TextBox>("Search")!.Watermark = Localization.Text("搜索");
        int projectsMode = Box("ProjectModeBox").SelectedIndex;
        int chatsMode = Box("ChatModeBox").SelectedIndex;
        Box("ProjectModeBox").ItemsSource = ModeKeys.Select(Localization.Text).ToArray();
        Box("ChatModeBox").ItemsSource = ModeKeys.Select(Localization.Text).ToArray();
        Box("ProjectModeBox").SelectedIndex = projectsMode >= 0 ? projectsMode : app.Settings.ProjectSortMode;
        Box("ChatModeBox").SelectedIndex = chatsMode >= 0 ? chatsMode : app.Settings.ChatSortMode;
        Box("LanguageBox").ItemsSource = new LanguageChoice[] { new("system", ""), new("zh-CN", "中文"), new("en", "English") };
        Box("LanguageBox").SelectedIndex = app.Settings.Language == "zh-CN" ? 1 : app.Settings.Language == "en" ? 2 : 0;
        updating = false;
    }

    public Task RefreshAsync() => RunAsync(async () =>
    {
        string? selected = Scope?.Id;
        scopes = await service.RefreshAsync();
        List<SortRow> projects = await service.GetRowsAsync(scopes[0]);
        workspace = WorkspacePreview.Create(service.Projects, projects, await service.ReadCatalogAsync());
        updating = true;
        Box("ScopeBox").ItemsSource = scopes;
        Box("ScopeBox").SelectedIndex = Math.Max(0, scopes.FindIndex(scope => scope.Id == selected));
        updating = false;
        if (expanded.Count == 0 && workspace.Groups.FirstOrDefault(group => group.Chats.Count > 0) is ProjectGroup first) expanded.Add(first.Scope.Id);
        await LoadScopeAsync();
        SetStatus(app.Status.Length > 0 ? app.Status : Localization.Text("已连接"));
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        await app.OperationGate.WaitAsync();
        Button("ApplyButton").IsEnabled = false;
        try { await action(); }
        catch (Exception error) { SetStatus(error.Message, true); }
        finally { app.OperationGate.Release(); busy = false; Button("ApplyButton").IsEnabled = true; }
    }

    private async Task LoadScopeAsync()
    {
        if (Scope?.Kind == "section")
        {
            sectionRows = await service.GetRowsAsync(Scope);
            originalSectionRows = sectionRows.ToList();
        }
        SortPreview();
    }

    private void ChangeModes()
    {
        if (ProjectMode != 0 || ChatMode != 0) PauseAutomatic();
        app.SaveSettings(app.Settings with { ProjectSortMode = ProjectMode, ChatSortMode = ChatMode });
        SortPreview();
    }

    private void PauseAutomatic()
    {
        if (!app.Settings.AutomaticNameSorting) return;
        app.SaveSettings(app.Settings with { AutomaticNameSorting = false });
        updating = true;
        Check("AutomaticBox").IsChecked = false;
        updating = false;
        SetStatus(Localization.Text("自动排序已暂停"));
    }

    private void SortPreview()
    {
        workspace?.SortProjects(ProjectMode);
        workspace?.SortChats(ChatMode);
        SorterService.Sort(sectionRows, ChatMode);
        Render();
    }

    private IEnumerable<ProjectGroup> Groups() => Scope?.Kind == "project"
        ? workspace?.Groups.Where(group => group.Scope.Id == Scope.Id) ?? [] : workspace?.Groups ?? [];

    private List<ProjectGroup> ChangedGroups() => Groups().Where(group => group.Scope.Source == "codex"
        && !group.Chats.Select(SorterService.RowKey).SequenceEqual(group.OriginalChats.Select(SorterService.RowKey))).ToList();

    private void Render()
    {
        if (workspace == null) return;
        string? selected = (RowList.SelectedItem as SidebarDisplayRow)?.Entry.Key;
        string query = this.FindControl<TextBox>("Search")!.Text ?? "";
        bool Matches(SortRow row) => query.Length == 0 || row.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        var rows = new List<SidebarDisplayRow>();
        if (Scope?.Kind == "section")
        {
            rows.AddRange(sectionRows.Where(Matches).Select((row, index) => Display(new(null, row, false, index + 1, 0))));
        }
        else
        {
            foreach (ProjectGroup group in Groups())
            {
                bool projectMatch = Matches(group.Row);
                List<SortRow> chats = projectMatch ? group.Chats : group.Chats.Where(Matches).ToList();
                if (!projectMatch && chats.Count == 0) continue;
                if (tab != 1)
                    rows.Add(Display(new(group, group.Row, true, workspace.Groups.IndexOf(group) + 1, workspace.OriginalProjectPosition(group))));
                if (tab == 1 || expanded.Contains(group.Scope.Id) || query.Length > 0 || Scope?.Kind == "project")
                    rows.AddRange(chats.Select(row => Display(new(group, row, false, group.Chats.IndexOf(row) + 1, WorkspacePreview.OriginalChatPosition(group, row)))));
            }
            if (tab != 0 && Scope?.Kind == "projects")
                rows.AddRange(workspace.LooseChats.Where(Matches).Select((row, index) => Display(new(null, row, false, index + 1, 0, true))));
        }
        RowList.ItemsSource = rows;
        RowList.SelectedItem = rows.FirstOrDefault(row => row.Entry.Key == selected);
        Button("ProjectsTab").Content = Localization.Text("项目") + " " + workspace.Groups.Count;
        Button("ChatsTab").Content = Localization.Text("对话") + " " + workspace.ChatCount;
        Button("AllTab").Content = Localization.Text("全部") + " " + (workspace.Groups.Count + workspace.ChatCount);
        RenderChecks();
    }

    private SidebarDisplayRow Display(TreeEntry entry)
    {
        string marker = entry.IsProject ? expanded.Contains(entry.Row.Id) ? "▼" : "▶" : entry.Position.ToString();
        string detail = entry.IsProject ? entry.Group!.Chats.Count + Localization.Text(" 个对话")
            : tab == 1 ? entry.Group?.Row.Title ?? Localization.Text("未分组") : entry.Row.IsPinned ? Localization.Text("已置顶") : "";
        string updated = entry.Row.UpdatedAt is long timestamp ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime.ToString("yyyy-MM-dd") : "";
        return new(entry, marker, entry.Row.Title, detail, updated, new Thickness(!entry.IsProject && tab != 1 ? 20 : 0, 0, 8, 0));
    }

    public void ToggleProject(object? sender, RoutedEventArgs args)
    {
        if ((sender as Control)?.DataContext is not SidebarDisplayRow row || !row.Entry.IsProject) return;
        if (!expanded.Remove(row.Entry.Row.Id)) expanded.Add(row.Entry.Row.Id);
        Render();
    }

    private void RenderChecks()
    {
        if (workspace == null) return;
        bool includeChats = Check("ApplyChatsBox").IsChecked == true || tab == 1 || Scope?.Kind == "project";
        List<PreviewChange> changes = Scope?.Kind == "section"
            ? sectionRows.Select((row, index) => new PreviewChange(row.Title,
                originalSectionRows.FindIndex(original => SorterService.RowKey(original) == SorterService.RowKey(row)) + 1,
                index + 1, Localization.Text("对话"))).Where(change => change.Before != change.After).ToList()
            : workspace.Changes(Scope?.Kind == "projects" && tab != 1, includeChats ? Groups() : []);
        Label("Pending").Text = changes.Count + Localization.Text(" 处变更待提交");
        Label("ReadCheck").Text = workspace.Groups.Count + Localization.Text(" 个项目") + " · " + workspace.ChatCount + Localization.Text(" 条对话");
        Label("PlanCheck").Text = Scope?.ToString() ?? "";
        this.FindControl<ItemsControl>("Changes")!.ItemsSource = changes.Select(change => change.Title + "  " + change.Before + " → " + change.After).ToList();
        Check("ApplyChatsBox").IsVisible = Scope?.Kind == "projects";
        Box("ProjectModeBox").IsEnabled = Scope?.Kind == "projects" && tab != 1;
    }

    private void MoveSelected(int direction, bool edge)
    {
        if (workspace == null || RowList.SelectedItem is not SidebarDisplayRow selected) return;
        PauseAutomatic();
        TreeEntry source = selected.Entry;
        if (Scope?.Kind == "section")
        {
            int from = sectionRows.FindIndex(row => SorterService.RowKey(row) == source.Key);
            int to = edge ? direction < 0 ? 0 : sectionRows.Count - 1 : Math.Clamp(from + direction, 0, sectionRows.Count - 1);
            SortRow item = sectionRows[from];
            sectionRows.RemoveAt(from);
            sectionRows.Insert(to, item);
        }
        else
        {
            List<SortRow> movable = source.IsProject ? workspace.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList()
                : source.Group?.Chats.Where(row => !row.IsPinned).ToList() ?? [];
            int from = movable.FindIndex(row => row.Id == source.Row.Id);
            if (from < 0) return;
            int to = edge ? direction < 0 ? 0 : movable.Count - 1 : Math.Clamp(from + direction, 0, movable.Count - 1);
            workspace.MoveTo(source, source with { Row = movable[to] });
        }
        updating = true;
        Box(source.IsProject ? "ProjectModeBox" : "ChatModeBox").SelectedIndex = 6;
        updating = false;
        Render();
    }

    private void Restore()
    {
        workspace?.Restore();
        sectionRows = originalSectionRows.ToList();
        PauseAutomatic();
        updating = true;
        Box("ProjectModeBox").SelectedIndex = 6;
        Box("ChatModeBox").SelectedIndex = 6;
        updating = false;
        Render();
    }

    private async Task ApplyAsync()
    {
        if (workspace == null || Scope == null) return;
        if (Scope.Kind == "projects")
        {
            bool includeChats = Check("ApplyChatsBox").IsChecked == true || tab == 1;
            List<ProjectGroup> changed = includeChats ? ChangedGroups() : [];
            bool projectsChanged = tab != 1 && workspace.Changes(true, []).Count > 0;
            bool modeChanged = includeChats && service.Snapshot["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() != (ChatMode == 4 ? "updated_at" : "manual");
            if (!projectsChanged && changed.Count == 0 && !modeChanged) { SetStatus(Localization.Text("当前顺序已是目标顺序")); return; }
            await service.ApplyWorkspaceAsync(workspace.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList(), projectsChanged,
                changed.Select(group => new ProjectChatPlan(group.Scope, group.Chats.Where(row => !row.IsPinned).ToList())).ToList(), ChatMode == 4, applyChatMode: includeChats);
            workspace.Accept(projectsChanged, changed);
        }
        else
        {
            List<SortRow> rows = Scope.Kind == "section" ? sectionRows : Groups().Single().Chats.Where(row => !row.IsPinned).ToList();
            await service.ApplyAsync(Scope, rows, ChatMode == 4);
            if (Scope.Kind == "section") originalSectionRows = sectionRows.ToList();
            else workspace.Accept(false, Groups());
        }
        Render();
        SetStatus(Localization.Text("已应用到客户端侧栏"));
    }

    public void SetStatus(string text, bool error = false)
    {
        Label("StatusText").Text = text;
        Label("StatusText").Foreground = new SolidColorBrush(Color.Parse(error ? "#A02923" : "#006C4D"));
    }

    public void SaveScreenshot(string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
        bitmap.Render(this);
        bitmap.Save(path);
    }
}
