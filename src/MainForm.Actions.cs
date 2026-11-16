namespace SidebarSorter;

internal sealed partial class MainForm
{
    private void AddSetting(Control control, int height, int topMargin = 0)
    {
        control.Height = height;
        control.Margin = new Padding(0, topMargin, 0, 5);
        settingsFlow.Controls.Add(control);
    }

    private void ResizeSettings()
    {
        int width = Math.Max(250, settingsFlow.ClientSize.Width - 20);
        foreach (Control control in settingsFlow.Controls) control.Width = width;
    }

    private void ConfigureCombo(ComboBox box)
    {
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.ItemHeight = 29;
        box.BackColor = Color.FromArgb(247, 249, 253);
        box.DrawItem += (_, eventArgs) =>
        {
            if (eventArgs.Index < 0) return;
            using var brush = new SolidBrush((eventArgs.State & DrawItemState.Selected) != 0 ? UiStyle.Selected : Color.FromArgb(247, 249, 253));
            eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
            Rectangle bounds = eventArgs.Bounds;
            bounds.Inflate(-9, 0);
            UiStyle.DrawText(eventArgs.Graphics, box.Items[eventArgs.Index]?.ToString() ?? "", bounds, UiStyle.Text);
        };
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        SetControlsEnabled(false);
        try { await action(); }
        catch (Exception error)
        {
            status.Text = error is OperationCanceledException ? Localization.Text("连接超时，请刷新后重试。") : error.Message;
            status.ForeColor = Color.FromArgb(160, 41, 35);
        }
        finally
        {
            busy = false;
            SetControlsEnabled(true);
            RenderChecks();
            if (closeWhenIdle) Close();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (Control control in new Control[] { refresh, apply, restore, scope, projectOrder, chatOrder, applyChatOrder, up, down, top, bottom, projectsTab, chatsTab, allTab, quickSort, grouping, language })
            control.Enabled = enabled;
        if (!enabled) return;
        projectOrder.Enabled = GlobalScope && currentTab != 1;
        applyChatOrder.Visible = GlobalScope;
        grouping.Enabled = GlobalScope && currentTab != 0;
    }

    private async Task RefreshDataAsync()
    {
        status.Text = Localization.Text("正在读取项目与对话");
        List<SortScope> scopes = await service.RefreshAsync();
        CatalogResult catalog = await service.ReadCatalogAsync();
        List<SortRow> projectRows = await service.GetRowsAsync(scopes[0]);
        workspace = WorkspacePreview.Create(service.Projects, projectRows, catalog);
        string? selectedId = CurrentScope?.Id;
        updating = true;
        scope.Items.Clear();
        scope.Items.AddRange(scopes.Cast<object>().ToArray());
        scope.SelectedIndex = Math.Max(0, scopes.FindIndex(item => item.Id == selectedId));
        projectOrder.SelectedIndex = settings.ProjectSortMode;
        chatOrder.SelectedIndex = settings.ChatSortMode;
        applyChatOrder.Checked = true;
        updating = false;
        chatRuleChanged = false;
        manualChatGroups.Clear();
        workspace.SortProjects(projectOrder.SelectedIndex);
        workspace.SortChats(chatOrder.SelectedIndex);
        if (expandedProjects.Count == 0)
        {
            ProjectGroup? first = workspace.Groups.FirstOrDefault(group => group.Chats.Count > 0);
            if (first != null) expandedProjects.Add("project:" + first.Scope.Id);
        }
        await LoadScopeAsync();
        status.Text = catalog.Problems.Count == 0 ? Localization.Text("已连接") : Localization.Text("部分来源待读取");
        status.ForeColor = catalog.Problems.Count == 0 ? Color.FromArgb(0, 108, 77) : Color.FromArgb(150, 85, 0);
    }

    private async Task LoadScopeAsync()
    {
        if (CurrentScope is not SortScope selected || workspace == null) return;
        if (selected.Kind == "section")
        {
            sectionRows = await service.GetRowsAsync(selected);
            originalSectionRows = sectionRows.ToList();
            SorterService.Sort(sectionRows, chatOrder.SelectedIndex);
        }
        if (selected.Kind != "projects") currentTab = 1;
        RenderPreview();
    }

    private async Task SwitchTabAsync(int tab)
    {
        currentTab = tab;
        if (!GlobalScope)
        {
            updating = true;
            scope.SelectedIndex = 0;
            updating = false;
            await LoadScopeAsync();
        }
        if (tab != 0) applyChatOrder.Checked = true;
        RenderPreview();
    }

    private void SortChatPreview()
    {
        if (CurrentScope?.Kind == "section") SorterService.Sort(sectionRows, chatOrder.SelectedIndex);
        else if (GlobalScope) workspace?.SortChats(chatOrder.SelectedIndex);
        else foreach (ProjectGroup group in ActiveGroups()) WorkspacePreview.SortGroupChats(group, chatOrder.SelectedIndex);
        status.Text = Localization.Text("预览已更新");
        status.ForeColor = UiStyle.Text;
        RenderPreview();
    }

    private IEnumerable<ProjectGroup> ActiveGroups()
    {
        if (workspace == null) return [];
        if (CurrentScope?.Kind == "project") return workspace.Groups.Where(group => group.Scope.Id == CurrentScope.Id);
        return workspace.Groups;
    }

    private IEnumerable<ProjectGroup> PlannedChatGroups()
    {
        if (workspace == null || CurrentScope == null || CurrentScope.Kind == "section") return [];
        if (CurrentScope.Kind == "project") return ActiveGroups();
        if (!ApplyChats) return [];
        var groups = ActiveGroups().Where(group => group.Scope.Source == "codex");
        if (chatRuleChanged || manualChatGroups.Count == 0) return groups;
        return groups.Where(group => manualChatGroups.Contains(group.Scope.Id));
    }

    private IEnumerable<ProjectGroup> ChangedChatGroups() => PlannedChatGroups().Where(group =>
        !group.Chats.Select(SorterService.RowKey).SequenceEqual(group.OriginalChats.Select(SorterService.RowKey)));

    private bool ChatSortModeChanged
    {
        get
        {
            if (!ApplyChats && CurrentScope?.Kind != "project" && CurrentScope?.Id != "pinned") return false;
            string key = CurrentScope?.Id == "pinned" ? "pinned" : "chats";
            string expected = chatOrder.SelectedIndex == 4 ? "updated_at" : "manual";
            return service.Snapshot["sidebarPreferences"]?["sorting"]?[key]?.GetValue<string>() != expected;
        }
    }

    private void RenderPreview(string? selectionKey = null)
    {
        if (workspace == null || CurrentScope == null) return;
        selectionKey ??= (tree.SelectedNode?.Tag as TreeEntry)?.Key;
        rendering = true;
        tree.BeginUpdate();
        tree.Nodes.Clear();
        string query = search.Text.Trim();
        bool section = CurrentScope.Kind == "section";
        tree.FlatView = section || (GlobalScope && currentTab != 0 && !grouped);
        if (section)
        {
            for (int index = 0; index < sectionRows.Count; index++)
            {
                SortRow row = sectionRows[index];
                if (!Matches(row.Title, query)) continue;
                int original = originalSectionRows.FindIndex(item => SorterService.RowKey(item) == SorterService.RowKey(row)) + 1;
                tree.Nodes.Add(MakeNode(new TreeEntry(null, row, false, index + 1, original)));
            }
        }
        else if (tree.FlatView)
        {
            if (currentTab == 2)
            {
                int projectIndex = 0;
                foreach (ProjectGroup group in ActiveGroups())
                {
                    projectIndex++;
                    if (Matches(group.Row.Title, query))
                        tree.Nodes.Add(MakeNode(new TreeEntry(group, group.Row, true, projectIndex, workspace.OriginalProjectPosition(group))));
                }
            }
            var flat = ActiveGroups().SelectMany(group => group.Chats.Select(row => (Group: (ProjectGroup?)group, Row: row)))
                .Concat(workspace.LooseChats.Select(row => (Group: (ProjectGroup?)null, Row: row))).ToList();
            var orderRows = flat.Select(item => item.Row).ToList();
            SorterService.Sort(orderRows, chatOrder.SelectedIndex);
            var byKey = flat.ToDictionary(item => SorterService.RowKey(item.Row));
            for (int index = 0; index < orderRows.Count; index++)
            {
                SortRow row = orderRows[index];
                var item = byKey[SorterService.RowKey(row)];
                if (!Matches(row.Title, query) && !Matches(item.Group?.Row.Title ?? "", query)) continue;
                tree.Nodes.Add(MakeNode(new TreeEntry(item.Group, row, false, index + 1, 0, item.Group == null)));
            }
        }
        else
        {
            foreach (ProjectGroup group in ActiveGroups())
            {
                int position = workspace.Groups.IndexOf(group) + 1;
                bool parentMatch = Matches(group.Row.Title, query);
                List<SortRow> filtered = parentMatch ? group.Chats : group.Chats.Where(row => Matches(row.Title, query)).ToList();
                if (!parentMatch && filtered.Count == 0) continue;
                if (GlobalScope && currentTab == 1 && group.Chats.Count == 0) continue;
                var root = MakeNode(new TreeEntry(group, group.Row, true, position, workspace.OriginalProjectPosition(group)));
                foreach (SortRow row in filtered)
                {
                    int rank = group.Chats.IndexOf(row) + 1;
                    root.Nodes.Add(MakeNode(new TreeEntry(group, row, false, rank, WorkspacePreview.OriginalChatPosition(group, row))));
                }
                tree.Nodes.Add(root);
                if (query.Length > 0 || expandedProjects.Contains("project:" + group.Scope.Id) || CurrentScope.Kind == "project") root.Expand();
            }
            if (GlobalScope && currentTab != 0 && workspace.LooseChats.Count > 0)
            {
                var loose = workspace.LooseChats.Where(row => Matches(row.Title, query) || Matches(Localization.Text("未分组对话"), query)).ToList();
                if (loose.Count > 0)
                {
                    var row = new SortRow("loose", Localization.Text("未分组对话"), loose.Max(item => item.UpdatedAt));
                    var root = MakeNode(new TreeEntry(null, row, true, 0, 0, true, workspace.LooseChats.Count));
                    foreach (SortRow chat in loose)
                        root.Nodes.Add(MakeNode(new TreeEntry(null, chat, false, workspace.LooseChats.IndexOf(chat) + 1, 0, true)));
                    tree.Nodes.Add(root);
                    if (query.Length > 0 || expandedProjects.Contains("project:loose")) root.Expand();
                }
            }
        }
        TreeNode? selected = Walk(tree.Nodes).FirstOrDefault(node => (node.Tag as TreeEntry)?.Key == selectionKey);
        tree.SelectedNode = selected ?? tree.Nodes.Cast<TreeNode>().FirstOrDefault(node => node.IsExpanded) ?? tree.Nodes.Cast<TreeNode>().FirstOrDefault();
        tree.EndUpdate();
        rendering = false;
        projectsTab.Text = Localization.Text("项目 ") + workspace.Groups.Count;
        bool complete = workspace.Problems.Count == 0;
        chatsTab.Text = complete ? Localization.Text("对话 ") + workspace.ChatCount : Localization.Text("对话 …");
        allTab.Text = complete ? Localization.Text("全部 ") + (workspace.Groups.Count + workspace.ChatCount) : Localization.Text("全部 …");
        projectsTab.Selected = currentTab == 0; chatsTab.Selected = currentTab == 1; allTab.Selected = currentTab == 2;
        listTitle.Text = GlobalScope ? (currentTab == 0 ? Localization.Text("项目与项目内对话") : currentTab == 1 ? Localization.Text("对话") : Localization.Text("全部条目")) : CurrentScope.Label;
        summary.Text = complete ? workspace.Groups.Count + Localization.Text(" 个项目 · ") + workspace.ChatCount + Localization.Text(" 条对话")
            : workspace.Groups.Count + Localization.Text(" 个项目 · 对话待读取");
        grouping.Text = grouped ? Localization.Text("分组  按项目") : Localization.Text("分组  平铺");
        quickSort.Text = Localization.Text("排序  ") + SortModes[(GlobalScope && currentTab != 1 ? projectOrder : chatOrder).SelectedIndex];
        ShowSelection();
        RenderChecks();
        if (!busy) SetControlsEnabled(true);
        tree.Parent?.Controls.Cast<Control>().FirstOrDefault(control => control is Panel && control != tree)?.Invalidate();
    }

    private static bool Matches(string text, string query) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static TreeNode MakeNode(TreeEntry entry) => new(entry.Row.Title)
    {
        Tag = entry, Name = entry.Key,
        ToolTipText = entry.Row.Title + Localization.Text("\n最近更新 ") + FullTime(entry.Row.UpdatedAt)
    };

    private static IEnumerable<TreeNode> Walk(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (TreeNode child in Walk(node.Nodes)) yield return child;
        }
    }

    private List<PreviewChange> CurrentChanges()
    {
        if (workspace == null) return [];
        if (CurrentScope?.Kind == "section")
        {
            var changes = new List<PreviewChange>();
            for (int index = 0; index < sectionRows.Count; index++)
            {
                SortRow row = sectionRows[index];
                int before = originalSectionRows.FindIndex(item => SorterService.RowKey(item) == SorterService.RowKey(row)) + 1;
                if (before != index + 1) changes.Add(new PreviewChange(row.Title, before, index + 1, Localization.Text("对话")));
            }
            return changes;
        }
        return workspace.Changes(ApplyProjects, PlannedChatGroups());
    }

    private void RenderChecks()
    {
        if (workspace == null) return;
        List<PreviewChange> changes = CurrentChanges();
        pending.Text = changes.Count == 0 ? ChatSortModeChanged ? Localization.Text("排序模式待提交") : Localization.Text("暂无顺序变更") : changes.Count + Localization.Text(" 处变更待提交");
        changeTitle.Text = Localization.Text("本次变更  ") + changes.Count;
        changesList.BeginUpdate();
        changesList.Items.Clear();
        changesList.Items.AddRange(changes.Cast<object>().ToArray());
        changesList.EndUpdate();
        readCheck.Text = workspace.Problems.Count == 0
            ? Localization.Text("已读取 ") + workspace.Groups.Count + Localization.Text(" 项目 / ") + workspace.ChatCount + Localization.Text(" 对话")
            : workspace.Problems.Count + Localization.Text(" 个来源待读取");
        readCheck.ForeColor = workspace.Problems.Count == 0 ? Color.FromArgb(0, 108, 77) : Color.FromArgb(160, 86, 0);
        int plannedChats = ChangedChatGroups().Sum(group => group.Chats.Count(row => !row.IsPinned));
        planCheck.Text = changes.Count == 0 && !ChatSortModeChanged ? Localization.Text("提交范围  无需提交")
            : changes.Count == 0 ? Localization.Text("提交范围  对话排序模式")
            : CurrentScope?.Kind == "section" ? Localization.Text("提交范围  ") + sectionRows.Count + Localization.Text(" 条分区对话")
            : ApplyChats || CurrentScope?.Kind == "project" ? Localization.Text("提交范围  ") + plannedChats + Localization.Text(" 条项目内对话") : Localization.Text("提交范围  项目列表顺序");
        planCheck.ForeColor = UiStyle.Text;
        sharedMode.Visible = ApplyChats || CurrentScope?.Kind == "project";
        ResizeSettings();
    }

    private void ShowSelection()
    {
        selectionInfo.Text = tree.SelectedNode?.Tag is TreeEntry entry
            ? entry.IsProject ? Localization.Text("已选 1 个项目") : Localization.Text("已选 1 条对话")
            : "";
    }

    private static string FullTime(long? value) => value == null ? Localization.Text("暂无记录") : DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private void DrawChange(object? sender, DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0 || changesList.Items[eventArgs.Index] is not PreviewChange change) return;
        using var brush = new SolidBrush((eventArgs.State & DrawItemState.Selected) != 0 ? UiStyle.Selected : Color.White);
        eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
        UiStyle.DrawText(eventArgs.Graphics, change.Before.ToString("00") + " → " + change.After.ToString("00") + "  " + change.Title,
            eventArgs.Bounds, UiStyle.Text);
    }

    private void ShowSortMenu()
    {
        var menu = new ContextMenuStrip { Font = UiStyle.Chinese };
        ComboBox target = GlobalScope && currentTab != 1 ? projectOrder : chatOrder;
        for (int index = 0; index < SortModes.Length; index++)
        {
            int mode = index;
            menu.Items.Add(new ToolStripMenuItem(SortModes[index], null, (_, _) => target.SelectedIndex = mode) { Checked = index == target.SelectedIndex });
        }
        menu.Show(quickSort, new Point(0, quickSort.Height));
    }

    private bool CanMove(TreeEntry entry)
    {
        if (CurrentScope?.Kind == "section") return true;
        return entry.IsProject ? ApplyProjects && entry.Group?.CanReorder == true
            : entry.Group?.Scope.Source == "codex" && entry.Group.IsLoaded && !entry.Row.IsPinned;
    }

    private void MoveSelected(int direction, bool edge)
    {
        if (busy || tree.SelectedNode?.Tag is not TreeEntry selected || !CanMove(selected)) return;
        var siblings = Walk(tree.Nodes).Select(node => node.Tag).OfType<TreeEntry>()
            .Where(entry => entry.IsProject == selected.IsProject && entry.Group == selected.Group && entry.IsLoose == selected.IsLoose).ToList();
        if (selected.IsProject) siblings = tree.Nodes.Cast<TreeNode>().Select(node => node.Tag).OfType<TreeEntry>().Where(CanMove).ToList();
        int index = siblings.FindIndex(entry => entry.Key == selected.Key);
        int target = edge ? (direction < 0 ? 0 : siblings.Count - 1) : index + direction;
        if (index < 0 || target < 0 || target >= siblings.Count || target == index) return;
        MoveEntries(selected, siblings[target]);
    }

    private (TreeEntry Source, TreeEntry Target)? FindDropEntries(DragEventArgs eventArgs)
    {
        if (busy || eventArgs.Data?.GetData(DataFormats.Text) is not string key) return null;
        TreeEntry? source = Walk(tree.Nodes).Select(node => node.Tag).OfType<TreeEntry>().FirstOrDefault(entry => entry.Key == key);
        TreeNode? node = tree.GetNodeAt(tree.PointToClient(new Point(eventArgs.X, eventArgs.Y)));
        if (source == null || node?.Tag is not TreeEntry target || !CanMove(source) || !CanMove(target) || source.Key == target.Key) return null;
        bool compatible = source.IsProject && target.IsProject || !source.IsProject && !target.IsProject && source.Group == target.Group;
        return compatible ? (source, target) : null;
    }

    private void MoveEntries(TreeEntry source, TreeEntry target)
    {
        bool moved;
        if (CurrentScope?.Kind == "section")
        {
            int from = sectionRows.FindIndex(row => SorterService.RowKey(row) == source.Key);
            int to = sectionRows.FindIndex(row => SorterService.RowKey(row) == target.Key);
            moved = from >= 0 && to >= 0 && from != to;
            if (moved) { SortRow row = sectionRows[from]; sectionRows.RemoveAt(from); sectionRows.Insert(to, row); }
        }
        else moved = workspace?.MoveTo(source, target) == true;
        if (!moved) return;
        updating = true;
        if (source.IsProject) projectOrder.SelectedIndex = 6;
        else
        {
            chatOrder.SelectedIndex = 6;
            if (source.Group != null) manualChatGroups.Add(source.Group.Scope.Id);
            if (GlobalScope) applyChatOrder.Checked = true;
        }
        updating = false;
        status.Text = Localization.Text("预览已更新");
        status.ForeColor = UiStyle.Text;
        RenderPreview(source.Key);
        tree.SelectedNode?.EnsureVisible();
    }

    private void RestorePreview()
    {
        if (busy || workspace == null) return;
        workspace.Restore();
        sectionRows = originalSectionRows.ToList();
        updating = true;
        projectOrder.SelectedIndex = 6;
        chatOrder.SelectedIndex = 6;
        applyChatOrder.Checked = true;
        updating = false;
        chatRuleChanged = false;
        manualChatGroups.Clear();
        RenderPreview();
        status.Text = Localization.Text("预览已还原");
        status.ForeColor = UiStyle.Text;
    }

    private async Task ApplyPreviewAsync()
    {
        if (workspace == null || CurrentScope == null) return;
        if (CurrentChanges().Count == 0 && !ChatSortModeChanged)
        {
            status.Text = Localization.Text("当前顺序已是目标顺序");
            status.ForeColor = Color.FromArgb(0, 108, 77);
            return;
        }
        status.Text = Localization.Text("正在应用排序");
        List<ProjectGroup> chatGroups = ChangedChatGroups().ToList();
        if (chatGroups.Any(group => !group.IsLoaded)) throw new InvalidOperationException(Localization.Text("对话列表仍有来源待读取，请刷新后应用对话排序。"));
        if (GlobalScope)
        {
            List<SortRow> projects = workspace.Groups.Where(group => group.CanReorder).Select(group => group.Row).ToList();
            var plans = chatGroups.Select(group => new ProjectChatPlan(group.Scope, group.Chats.Where(row => !row.IsPinned).ToList())).ToList();
            bool projectsChanged = ApplyProjects && workspace.Changes(true, []).Count > 0;
            int submitted = 0;
            try
            {
                await service.ApplyWorkspaceAsync(projects, projectsChanged, plans, chatOrder.SelectedIndex == 4,
                    (completed, total) => { submitted = completed; status.Text = Localization.Text("正在提交 ") + completed + " / " + total; },
                    applyChatMode: ApplyChats);
            }
            catch (Exception error) when (submitted > 0)
            {
                throw new InvalidOperationException(Localization.Text("已提交 ") + submitted + Localization.Text(" 项，请刷新实际顺序。") + error.Message, error);
            }
            workspace.Accept(projectsChanged, chatGroups);
        }
        else
        {
            List<SortRow> rows = CurrentScope.Kind == "section" ? sectionRows : ActiveGroups().Single().Chats.Where(row => !row.IsPinned).ToList();
            await service.ApplyAsync(CurrentScope, rows, chatOrder.SelectedIndex == 4);
            if (CurrentScope.Kind == "section") originalSectionRows = sectionRows.ToList();
            else workspace.Accept(false, ActiveGroups());
        }
        chatRuleChanged = false;
        manualChatGroups.Clear();
        RenderPreview();
        status.Text = Localization.Text("已应用到客户端侧栏");
        status.ForeColor = Color.FromArgb(0, 108, 77);
    }
}
