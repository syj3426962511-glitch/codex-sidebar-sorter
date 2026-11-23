using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    private static readonly string[] ModeKeys = ["Name A to Z", "Name Z to A", "Natural numeric order", "Reverse natural order", "Recently updated first", "Oldest update first", "Manual order"];
    private static readonly ClientSystem?[] Systems = [null, ClientSystem.Windows, ClientSystem.MacOS, ClientSystem.Linux];
    private readonly DesktopApp app;
    private SorterService service = new();
    private readonly HashSet<string> expanded = new();
    private WorkspacePreview? workspace;
    private List<SortScope> scopes = [];
    private List<SortRow> sectionRows = [];
    private List<SortRow> originalSectionRows = [];
    private SortScope? loadedScope;
    private bool needsRefresh = true;
    private bool updating;
    private bool busy;
    private static readonly DataFormat<SidebarDisplayRow> RowFormat = DataFormat.CreateInProcessFormat<SidebarDisplayRow>("sidebar-sorter-row");
    private PointerPressedEventArgs? dragPress;
    private SidebarDisplayRow? dragRow;
    private Point dragOrigin;
    private int tab;
    private int systemIndex;
    private ClientSystem SelectedSystem => Systems[systemIndex] ?? NativeBridge.CurrentSystem;
    private bool CanUseSelectedSystem => SelectedSystem == NativeBridge.CurrentSystem;
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
        Label("VersionText").Text = typeof(DesktopWindow).Assembly.GetName().Version?.ToString(3);
        DragDrop.SetAllowDrop(RowList, true);
        DragDrop.AddDragOverHandler(RowList, (_, args) =>
        {
            var source = args.DataTransfer.TryGetValue(RowFormat);
            var target = (args.Source as Control)?.DataContext as SidebarDisplayRow;
            args.DragEffects = source != null && target != null && CanMove(source.Entry, target.Entry) ? DragDropEffects.Move : DragDropEffects.None;
            args.Handled = true;
        });
        DragDrop.AddDropHandler(RowList, (_, args) =>
        {
            if (args.DataTransfer.TryGetValue(RowFormat) is SidebarDisplayRow source
                && (args.Source as Control)?.DataContext is SidebarDisplayRow target)
                MoveRow(source.Entry, target.Entry);
            args.Handled = true;
        });
        SetCaptions();
        Box("SystemBox").SelectionChanged += async (_, _) =>
        {
            int index = Box("SystemBox").SelectedIndex;
            if (updating || busy || index < 0 || index == systemIndex) return;
            systemIndex = index;
            service = new SorterService(system: SelectedSystem);
            ClearWorkspace();
            UpdateControls();
            SetStatus("");
            await RefreshAsync();
        };
        Box("LanguageBox").SelectionChanged += (_, _) =>
        {
            if (updating || Box("LanguageBox").SelectedItem is not LanguageChoice choice) return;
            app.SaveSettings(app.Settings with { Language = choice.Code });
            SetCaptions();
            Render();
        };
        Box("ScopeBox").SelectionChanged += async (_, _) =>
        {
            if (!updating && !needsRefresh) await RunAsync(LoadScopeAsync);
        };
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
            SetStatus(Localization.Text(enabled ? "Keep names sorted A to Z" : "Automatic sorting is paused"));
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
        Button("ApplyButton").Click += async (_, _) => await RunAsync(ApplyAsync, refreshOnError: true);
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            string[] args = DesktopProgram.Arguments;
            if (args.Length == 2 && args[0] == "--capture")
            {
                await Task.Delay(200);
                SaveScreenshot(args[1]);
                await app.ExitAsync();
            }
        };
    }

    private void SetCaptions()
    {
        updating = true;
        string status = Localization.SourceText(Label("StatusText").Text ?? "");
        FlowDirection = Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = Localization.Text("Codex Sidebar Sorter");
        foreach (var (name, text) in new (string, string)[]
        {
            ("Heading","Sidebar Sorter"),("SystemLabel","System"),("LanguageLabel","Language"),("ScopeLabel","Scope"),("ListTitle","Projects and their chats"),
            ("NameColumn","Name"),("DetailColumn","Chats / status"),("TimeColumn","Last updated"),("SortHeading","Sorting and changes"),
            ("ProjectModeLabel","Projects (section)"),("ChatModeLabel","Chats within each project"),
            ("SharedModeLabel","Project chats and Recents share the saved sort mode"),("CheckHeading","Before applying")
        }) Label(name).Text = Localization.Text(text);
        foreach (var (name, text) in new (string, string)[]
        {
            ("RefreshButton","Refresh"),("UpButton","\u2191 Move up"),("DownButton","\u2193 Move down"),("TopButton","To top"),
            ("BottomButton","To bottom"),("RestoreButton","Reset preview"),("HideButton","Hide"),("ApplyButton","Apply to sidebar")
        }) Button(name).Content = Localization.Text(text);
        Check("AutomaticBox").Content = Localization.Text("Keep names sorted A to Z");
        Check("AutomaticBox").IsChecked = app.Settings.AutomaticNameSorting;
        Check("ApplyChatsBox").Content = Localization.Text("Also sort chats in each project");
        this.FindControl<TextBox>("Search")!.PlaceholderText = Localization.Text("Search");
        int projectsMode = Box("ProjectModeBox").SelectedIndex;
        int chatsMode = Box("ChatModeBox").SelectedIndex;
        Box("ProjectModeBox").ItemsSource = ModeKeys.Select(Localization.Text).ToArray();
        Box("ChatModeBox").ItemsSource = ModeKeys.Select(Localization.Text).ToArray();
        Box("ProjectModeBox").SelectedIndex = projectsMode >= 0 ? projectsMode : app.Settings.ProjectSortMode;
        Box("ChatModeBox").SelectedIndex = chatsMode >= 0 ? chatsMode : app.Settings.ChatSortMode;
        Box("SystemBox").ItemsSource = Systems.Select(system => system is ClientSystem explicitSystem
            ? NativeBridge.SystemName(explicitSystem)
            : string.Format(Localization.Text("Auto ({0})"), NativeBridge.SystemName(NativeBridge.CurrentSystem))).ToArray();
        Box("SystemBox").SelectedIndex = systemIndex;
        Box("LanguageBox").ItemsSource = Localization.Choices;
        Box("LanguageBox").SelectedItem = Localization.Choices.First(choice => choice.Code == app.Settings.Language);
        SortScope? selectedScope = Scope;
        Box("ScopeBox").ItemsSource = scopes.ToArray();
        Box("ScopeBox").SelectedItem = selectedScope;
        Label("StatusText").Text = Localization.Text(status);
        if (!CanUseSelectedSystem) SetStatus("");
        UpdateTabCounts();
        updating = false;
    }

    private void ClearWorkspace()
    {
        needsRefresh = true;
        workspace = null;
        loadedScope = null;
        scopes = [];
        sectionRows = [];
        originalSectionRows = [];
        expanded.Clear();
        Box("ScopeBox").ItemsSource = scopes;
        RowList.ItemsSource = null;
        UpdateTabCounts();
        foreach (string label in new[] { "Pending", "ReadCheck", "PlanCheck" }) Label(label).Text = "";
        this.FindControl<ItemsControl>("Changes")!.ItemsSource = null;
    }

    public Task RefreshAsync(bool refreshChatGptHistory = true) => RunAsync(async () =>
    {
        needsRefresh = true;
        SortScope? selected = loadedScope;
        scopes = await service.RefreshAsync();
        List<SortRow> projects = await service.GetRowsAsync(scopes[0]);
        workspace = WorkspacePreview.Create(service.Projects, projects, await service.ReadCatalogAsync(refreshChatGptHistory));
        updating = true;
        Box("ScopeBox").ItemsSource = scopes;
        Box("ScopeBox").SelectedIndex = Math.Max(0, scopes.FindIndex(scope =>
            selected != null && scope.Id == selected.Id && scope.Kind == selected.Kind
            && scope.HostId == selected.HostId && scope.Source == selected.Source));
        updating = false;
        await LoadScopeAsync();
        needsRefresh = false;
        if (expanded.Count == 0 && workspace.Groups.FirstOrDefault(group => group.Chats.Count > 0) is ProjectGroup first)
        {
            expanded.Add(first.Scope.Id);
            Render();
        }
        if (workspace.Problems.Count > 0)
            SetStatus(string.Join("\n", workspace.Problems.Values.Select(error => Localization.Text(error.Message)).Distinct()), true);
        else
            SetStatus(app.Status.Length > 0 ? app.Status : Localization.Text("Connected"));
    });

    private async Task RunAsync(Func<Task> action, bool refreshOnError = false)
    {
        if (busy || app.IsClosing || !CanUseSelectedSystem) return;
        busy = true;
        UpdateControls();
        await app.OperationGate.WaitAsync();
        try
        {
            await using var lease = await BackgroundClient.AcquireAsync();
            await action();
        }
        catch (Exception error)
        {
            if (refreshOnError) needsRefresh = true;
            SetStatus(error.Message, true);
        }
        finally
        {
            app.OperationGate.Release();
            busy = false;
            UpdateControls();
        }
    }

    private async Task LoadScopeAsync()
    {
        SortScope? scope = Scope;
        loadedScope = null;
        sectionRows = [];
        originalSectionRows = [];
        if (scope?.Kind == "section")
        {
            Render();
            sectionRows = await service.GetRowsAsync(scope);
            originalSectionRows = sectionRows.ToList();
        }
        loadedScope = scope;
        SortPreview();
    }

    private void UpdateControls()
    {
        bool ready = !busy && !needsRefresh && loadedScope != null && Scope == loadedScope;
        Box("ScopeBox").IsEnabled = !busy && !needsRefresh;
        Box("LanguageBox").IsEnabled = !busy;
        Box("SystemBox").IsEnabled = !busy;
        Box("ProjectModeBox").IsEnabled = ready && Scope?.Kind == "projects" && tab != 1;
        Box("ChatModeBox").IsEnabled = ready;
        Check("ApplyChatsBox").IsEnabled = ready;
        Check("AutomaticBox").IsEnabled = !busy && CanUseSelectedSystem;
        Button("RefreshButton").IsEnabled = !busy && CanUseSelectedSystem;
        foreach (string name in new[] { "ProjectsTab", "ChatsTab", "AllTab" })
            Button(name).IsEnabled = !busy;
        foreach (string name in new[] { "UpButton", "DownButton", "TopButton", "BottomButton", "RestoreButton" })
            Button(name).IsEnabled = ready;
        Button("ApplyButton").IsEnabled = ready && (Scope?.Kind != "project" || Groups().Any(group => group.IsLoaded));
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
        SetStatus(Localization.Text("Automatic sorting is paused"));
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

    private List<ProjectGroup> ChangedGroups() => Groups().Where(group => group.IsLoaded && group.Scope.Source == "codex"
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
        UpdateTabCounts();
        RenderChecks();
    }

    private void UpdateTabCounts()
    {
        int projects = workspace?.Groups.Count ?? 0;
        int chats = workspace?.ChatCount ?? 0;
        Button("ProjectsTab").Content = Localization.Text("Projects") + " " + projects;
        Button("ChatsTab").Content = Localization.Text("Chats") + " " + chats;
        Button("AllTab").Content = Localization.Text("All") + " " + (projects + chats);
    }

    private SidebarDisplayRow Display(TreeEntry entry)
    {
        string marker = entry.IsProject ? expanded.Contains(entry.Row.Id) ? "▼" : "▶" : entry.Position.ToString();
        string detail = entry.IsProject ? entry.Group!.Chats.Count + Localization.Text(" chats (count)")
            : tab == 1 ? entry.Group?.Row.Title ?? Localization.Text("Ungrouped") : entry.Row.IsPinned ? Localization.Text("Pinned") : "";
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
                index + 1, Localization.Text("Chats"))).Where(change => change.Before != change.After).ToList()
            : workspace.Changes(Scope?.Kind == "projects" && tab != 1, includeChats ? Groups() : []);
        Label("Pending").Text = changes.Count + Localization.Text(" pending changes");
        Label("ReadCheck").Text = workspace.Groups.Count + Localization.Text(" projects") + " · " + workspace.ChatCount + Localization.Text(" chats");
        if (workspace.Problems.Count > 0)
            Label("ReadCheck").Text += " · " + Localization.Text("Some chat history could not be refreshed.");
        Label("PlanCheck").Text = Scope?.ToString() ?? "";
        this.FindControl<ItemsControl>("Changes")!.ItemsSource = changes.Select(change => change.Title + "  " + change.Before + " → " + change.After).ToList();
        Check("ApplyChatsBox").IsVisible = Scope?.Kind == "projects";
        UpdateControls();
    }

    private void MoveSelected(int direction, bool edge)
    {
        if (workspace == null || RowList.SelectedItem is not SidebarDisplayRow selected) return;
        TreeEntry source = selected.Entry;
        List<SortRow> movable = Scope?.Kind == "section" ? sectionRows
            : source.IsProject ? workspace.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList()
            : source.Group?.Chats.Where(row => !row.IsPinned).ToList() ?? [];
        int from = movable.FindIndex(row => row.Id == source.Row.Id);
        if (from < 0) return;
        int to = edge ? direction < 0 ? 0 : movable.Count - 1 : Math.Clamp(from + direction, 0, movable.Count - 1);
        MoveRow(source, source with { Row = movable[to] });
    }

    private bool CanMove(TreeEntry source, TreeEntry target) => !busy && !needsRefresh && Scope == loadedScope && source.Key != target.Key
        && (Scope?.Kind == "section" || source.IsProject && target.IsProject && source.Group?.CanReorder == true && target.Group?.CanReorder == true
            || !source.IsProject && !target.IsProject && source.Group != null && source.Group == target.Group
            && source.Group.IsLoaded && source.Group.Scope.Source == "codex" && !source.Row.IsPinned && !target.Row.IsPinned);

    private void MoveRow(TreeEntry source, TreeEntry target)
    {
        if (workspace == null || !CanMove(source, target)) return;
        PauseAutomatic();
        if (Scope?.Kind == "section")
        {
            int from = sectionRows.FindIndex(row => SorterService.RowKey(row) == source.Key);
            int to = sectionRows.FindIndex(row => SorterService.RowKey(row) == target.Key);
            if (from < 0 || to < 0) return;
            SortRow item = sectionRows[from];
            sectionRows.RemoveAt(from);
            sectionRows.Insert(to, item);
        }
        else
        {
            if (!workspace.MoveTo(source, target)) return;
        }
        updating = true;
        Box(source.IsProject ? "ProjectModeBox" : "ChatModeBox").SelectedIndex = 6;
        updating = false;
        app.SaveSettings(app.Settings with { ProjectSortMode = ProjectMode, ChatSortMode = ChatMode });
        Render();
    }

    public void RowPressed(object? sender, PointerPressedEventArgs args)
    {
        if (busy || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragPress = args;
        dragOrigin = args.GetPosition(this);
        dragRow = (sender as Control)?.DataContext as SidebarDisplayRow;
    }

    public async void RowMoved(object? sender, PointerEventArgs args)
    {
        if (dragPress == null || dragRow == null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Point distance = args.GetPosition(this) - dragOrigin;
        if (Math.Abs(distance.X) < 6 && Math.Abs(distance.Y) < 6) return;
        PointerPressedEventArgs pressed = dragPress;
        SidebarDisplayRow row = dragRow;
        dragPress = null;
        dragRow = null;
        RowList.SelectedItem = row;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RowFormat, row));
        await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
    }

    public void RowReleased(object? sender, PointerReleasedEventArgs args) { dragPress = null; dragRow = null; }

    private void Restore()
    {
        workspace?.Restore();
        sectionRows = originalSectionRows.ToList();
        PauseAutomatic();
        updating = true;
        Box("ProjectModeBox").SelectedIndex = 6;
        Box("ChatModeBox").SelectedIndex = 6;
        updating = false;
        app.SaveSettings(app.Settings with { ProjectSortMode = ProjectMode, ChatSortMode = ChatMode });
        Render();
    }

    private async Task ApplyAsync()
    {
        SortScope? scope = loadedScope;
        if (workspace == null || scope == null || scope != Scope || needsRefresh) return;
        if (scope.Kind == "projects")
        {
            bool includeChats = Check("ApplyChatsBox").IsChecked == true || tab == 1;
            List<ProjectGroup> changed = includeChats ? ChangedGroups() : [];
            bool projectsChanged = tab != 1 && workspace.Changes(true, []).Count > 0;
            bool modeChanged = includeChats && Groups().Any(group => group.IsLoaded && group.Scope.Source == "codex" && group.Chats.Count > 0)
                && service.Snapshot["sidebarPreferences"]?["sorting"]?["chats"]?.GetValue<string>() != "manual";
            if (!projectsChanged && changed.Count == 0 && !modeChanged) { SetStatus(Localization.Text("The requested order is already applied")); return; }
            await service.ApplyWorkspaceAsync(workspace.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList(), projectsChanged,
                changed.Select(group => new ProjectChatPlan(group.Scope, group.Chats.Where(row => !row.IsPinned).ToList())).ToList(), recentMode: false, applyChatMode: modeChanged);
            workspace.Accept(projectsChanged, changed);
        }
        else
        {
            if (scope.Kind == "project" && !Groups().Any(group => group.IsLoaded)) return;
            List<SortRow> rows = scope.Kind == "section" ? sectionRows : Groups().Single().Chats.Where(row => !row.IsPinned).ToList();
            if (rows.Count == 0) { SetStatus(Localization.Text("The requested order is already applied")); return; }
            await service.ApplyAsync(scope, rows, recentMode: false);
            if (scope.Kind == "section") originalSectionRows = sectionRows.ToList();
            else workspace.Accept(false, Groups());
        }
        Render();
        SetStatus(Localization.Text("Applied to the Codex sidebar"));
    }

    public void SetStatus(string text, bool error = false)
    {
        if (!CanUseSelectedSystem)
        {
            text = NativeBridge.LocalSystemPrompt();
            error = false;
        }
        Label("StatusText").Text = Localization.Text(Localization.SourceText(text));
        Label("StatusText").Foreground = new SolidColorBrush(Color.Parse(error ? "#A02923" : "#006C4D"));
    }

    public void SaveScreenshot(string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
        bitmap.Render(this);
        bitmap.Save(path);
    }
}
