using System.Drawing.Imaging;

namespace SidebarSorter;

internal sealed partial class MainForm : Form
{
    private static readonly string[] SortModeKeys =
        ["名称升序", "名称降序", "数字自然升序", "数字自然降序", "最近更新优先", "最早更新优先", "手动顺序"];
    private static string[] SortModes => SortModeKeys.Select(Localization.Text).ToArray();
    private readonly SorterService service = new();
    private readonly ComboBox scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 740 };
    private readonly ComboBox projectOrder = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox chatOrder = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox applyChatOrder = new() { Text = Localization.Text("同时应用项目内对话排序"), AutoSize = false };
    private readonly ConversationTree tree = new() { Dock = DockStyle.Fill };
    private readonly ViewTab projectsTab = new();
    private readonly ViewTab chatsTab = new();
    private readonly ViewTab allTab = new();
    private readonly TextBox search = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Button refresh = new() { Text = Localization.Text("刷新") };
    private readonly Button quickSort = new();
    private readonly Button grouping = new() { Text = Localization.Text("分组  按项目") };
    private readonly Button up = new() { Text = Localization.Text("↑ 上移") };
    private readonly Button down = new() { Text = Localization.Text("↓ 下移") };
    private readonly Button top = new() { Text = Localization.Text("移至顶部") };
    private readonly Button bottom = new() { Text = Localization.Text("移至底部") };
    private readonly Button restore = new() { Text = Localization.Text("还原预览") };
    private readonly Button cancel = new() { Text = Localization.Text("收起") };
    private readonly Button apply = new() { Text = Localization.Text("应用到侧栏") };
    private readonly MixedLabel summary = new();
    private readonly MixedLabel listTitle = new();
    private readonly MixedLabel pending = new();
    private readonly MixedLabel changeTitle = new();
    private readonly MixedLabel readCheck = new();
    private readonly MixedLabel planCheck = new();
    private readonly MixedLabel status = new() { Text = Localization.Text("正在连接客户端") };
    private readonly MixedLabel selectionInfo = new();
    private readonly ListBox changesList = new() { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27 };
    private readonly Label sharedMode = new() { Text = Localization.Text("项目内对话与最近聊天共用排序模式"), Height = 64 };
    private readonly FlowLayoutPanel settingsFlow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White };
    private readonly HashSet<string> expandedProjects = new();
    private readonly HashSet<string> manualChatGroups = new();
    private readonly string? capturePath;
    private WorkspacePreview? workspace;
    private List<SortRow> sectionRows = [];
    private List<SortRow> originalSectionRows = [];
    private int currentTab;
    private bool grouped = true;
    private bool updating;
    private bool rendering;
    private bool busy;
    private bool chatRuleChanged;
    private readonly bool returnToBackground;
    private bool closeWhenIdle;

    protected override bool ShowWithoutActivation => true;
    private SortScope? CurrentScope => scope.SelectedItem as SortScope;
    private bool GlobalScope => CurrentScope?.Kind == "projects";
    private bool ApplyProjects => GlobalScope && currentTab != 1;
    private bool ApplyChats => GlobalScope && (currentTab == 1 || applyChatOrder.Checked);

    public MainForm(string? capturePath, bool returnToBackground = false)
    {
        LoadToolPreferences();
        this.capturePath = capturePath;
        this.returnToBackground = returnToBackground;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Localization.Text("聊天排序工具");
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1120, workingArea.Width), Math.Min(780, workingArea.Height));
        ClientSize = new Size(Math.Min(1480, workingArea.Width - 24), Math.Min(980, workingArea.Height - 60));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiStyle.Background;
        ForeColor = UiStyle.Text;
        Font = UiStyle.Chinese;
        KeyPreview = true;
        if (capturePath != null || Environment.GetEnvironmentVariable("CHAT_SORTER_TEST_HIDE_WINDOW") == "1")
        { Opacity = 0; ShowInTaskbar = false; }

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 12), ColumnCount = 1, RowCount = 5 };
        foreach (float height in new float[] { 46, 54, 56 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        using Stream logoStream = typeof(MainForm).Assembly.GetManifestResourceStream("SidebarSorter.BrandLogo")!;
        using Image logoImage = Image.FromStream(logoStream);
        heading.Controls.Add(new PictureBox
        {
            Image = new Bitmap(logoImage), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(38, 38), Margin = new Padding(0, 3, 12, 0)
        });
        heading.Controls.Add(new Label { Text = Localization.Text("聊天排序"), AutoSize = true, Font = new Font("SimSun", 17, FontStyle.Bold), Margin = new Padding(0, 5, 18, 0) });
        heading.Controls.Add(new Label { Text = Application.ProductVersion.Split('+')[0], AutoSize = true, Font = UiStyle.English, Margin = new Padding(0, 11, 0, 0) });
        AddLanguageControls(heading);
        layout.Controls.Add(heading, 0, 0);

        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        tabs.Controls.AddRange([projectsTab, chatsTab, allTab]);
        navigation.Controls.Add(tabs, 0, 0);
        navigation.Controls.Add(new MixedLabel { Text = Localization.Text("搜索"), Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 3, 0, 0) }, 1, 0);
        search.Dock = DockStyle.Top;
        search.Margin = new Padding(0, 6, 12, 0);
        navigation.Controls.Add(search, 2, 0);
        refresh.Dock = DockStyle.Top;
        refresh.Height = 38;
        refresh.Margin = new Padding(0, 3, 0, 0);
        navigation.Controls.Add(refresh, 3, 0);
        layout.Controls.Add(navigation, 0, 1);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        quickSort.Dock = DockStyle.Top; quickSort.Height = 39;
        grouping.Dock = DockStyle.Top; grouping.Height = 39;
        toolbar.Controls.Add(quickSort, 0, 0);
        toolbar.Controls.Add(grouping, 1, 0);
        toolbar.Controls.Add(new MixedLabel { Text = Localization.Text("范围"), Dock = DockStyle.Top, Height = 39 }, 2, 0);
        ConfigureCombo(scope);
        scope.Dock = DockStyle.Top;
        toolbar.Controls.Add(scope, 3, 0);
        summary.Dock = DockStyle.Fill;
        summary.Margin = new Padding(16, 0, 0, 8);
        toolbar.Controls.Add(summary, 4, 0);
        layout.Controls.Add(toolbar, 0, 2);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, BackColor = UiStyle.Background };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 398));
        var listCard = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16) };
        var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.White, Margin = Padding.Empty };
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        listTitle.Dock = DockStyle.Fill;
        listLayout.Controls.Add(listTitle, 0, 0);
        var columns = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        columns.Paint += (_, eventArgs) =>
        {
            (int count, int time) = tree.Columns();
            UiStyle.DrawText(eventArgs.Graphics, Localization.Text("名称"), new Rectangle(80, 0, count - 90, 32), UiStyle.Text, true);
            UiStyle.DrawText(eventArgs.Graphics, tree.FlatView ? Localization.Text("所属项目") : Localization.Text("对话 / 状态"), new Rectangle(count + 8, 0, time - count - 14, 32), UiStyle.Text);
            UiStyle.DrawText(eventArgs.Graphics, Localization.Text("最近更新"), new Rectangle(time + 8, 0, 160, 32), UiStyle.Text);
        };
        tree.Resize += (_, _) => columns.Invalidate();
        listLayout.Controls.Add(columns, 0, 1);
        listLayout.Controls.Add(tree, 0, 2);
        var movement = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = Padding.Empty, Padding = new Padding(0, 13, 0, 0) };
        foreach (Button button in new[] { up, down, top, bottom })
        {
            button.Size = new Size(112, 38);
            button.Margin = new Padding(0, 0, 9, 0);
            movement.Controls.Add(button);
        }
        selectionInfo.Size = new Size(260, 38);
        selectionInfo.Margin = new Padding(10, 0, 0, 0);
        movement.Controls.Add(selectionInfo);
        listLayout.Controls.Add(movement, 0, 3);
        listCard.Controls.Add(listLayout);
        body.Controls.Add(listCard, 0, 0);

        var settingsCard = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(20, 16, 20, 16) };
        AddSetting(new Label { Text = Localization.Text("排序与变更"), Font = new Font("SimSun", 16, FontStyle.Bold) }, 38);
        pending.ForeColor = UiStyle.Accent;
        AddSetting(pending, 28);
        AddSetting(new Label { Text = Localization.Text("第一层 · 项目") }, 26);
        ConfigureCombo(projectOrder);
        AddSetting(projectOrder, 40);
        AddSetting(new Label { Text = Localization.Text("第二层 · 项目内对话") }, 26);
        ConfigureCombo(chatOrder);
        AddSetting(chatOrder, 40);
        AddSetting(applyChatOrder, 34);
        AddSetting(sharedMode, 64);
        AddSetting(new Label { Text = Localization.Text("可用排序"), Font = UiStyle.ChineseBold }, 28, 9);
        AddSetting(new Label { Text = Localization.Text("名称 · 数字自然\n更新时间 · 手动") }, 60);
        AddSetting(changeTitle, 28, 8);
        AddSetting(changesList, 94);
        AddSetting(new Label { Text = Localization.Text("提交前检查"), Font = UiStyle.ChineseBold }, 28, 8);
        AddSetting(readCheck, 28);
        AddSetting(planCheck, 48);
        settingsFlow.Resize += (_, _) => ResizeSettings();
        settingsCard.Controls.Add(settingsFlow);
        body.Controls.Add(settingsCard, 2, 0);
        layout.Controls.Add(body, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        status.Dock = DockStyle.Fill;
        footer.Controls.Add(status, 0, 0);
        foreach (var item in new[] { (restore, 1), (cancel, 2), (apply, 3) })
        {
            item.Item1.Dock = DockStyle.Fill;
            item.Item1.Margin = new Padding(8, 14, 0, 6);
            footer.Controls.Add(item.Item1, item.Item2, 0);
        }
        foreach (Button button in new[] { refresh, quickSort, grouping, up, down, top, bottom, restore, cancel }) UiStyle.ConfigureButton(button);
        UiStyle.ConfigureButton(apply, true);
        layout.Controls.Add(footer, 0, 4);
        Controls.Add(layout);

        projectOrder.Items.AddRange(SortModes);
        chatOrder.Items.AddRange(SortModes);
        projectOrder.SelectedIndex = settings.ProjectSortMode;
        chatOrder.SelectedIndex = settings.ChatSortMode;
        projectsTab.Text = Localization.Text("项目"); chatsTab.Text = Localization.Text("对话"); allTab.Text = Localization.Text("全部");
        projectsTab.Selected = true;

        refresh.Click += async (_, _) => await RunAsync(RefreshDataAsync);
        projectsTab.Click += async (_, _) => await RunAsync(() => SwitchTabAsync(0));
        chatsTab.Click += async (_, _) => await RunAsync(() => SwitchTabAsync(1));
        allTab.Click += async (_, _) => await RunAsync(() => SwitchTabAsync(2));
        scope.SelectedIndexChanged += async (_, _) => { if (!updating) await RunAsync(LoadScopeAsync); };
        projectOrder.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            workspace?.SortProjects(projectOrder.SelectedIndex);
            status.Text = Localization.Text("预览已更新");
            status.ForeColor = UiStyle.Text;
            RenderPreview();
        };
        chatOrder.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            chatRuleChanged = true;
            if (GlobalScope) applyChatOrder.Checked = true;
            SortChatPreview();
        };
        applyChatOrder.CheckedChanged += (_, _) => { if (!updating) RenderChecks(); };
        grouping.Click += (_, _) => { grouped = !grouped; RenderPreview(); };
        search.TextChanged += (_, _) => RenderPreview();
        quickSort.Click += (_, _) => ShowSortMenu();
        up.Click += (_, _) => MoveSelected(-1, false);
        down.Click += (_, _) => MoveSelected(1, false);
        top.Click += (_, _) => MoveSelected(-1, true);
        bottom.Click += (_, _) => MoveSelected(1, true);
        restore.Click += (_, _) => RestorePreview();
        cancel.Click += (_, _) => Close();
        apply.Click += async (_, _) => await RunAsync(ApplyPreviewAsync);
        tree.AfterSelect += (_, _) => { if (!rendering) ShowSelection(); };
        tree.AfterExpand += (_, eventArgs) => { if (!rendering && eventArgs.Node?.Tag is TreeEntry entry && entry.IsProject) expandedProjects.Add(entry.Key); };
        tree.AfterCollapse += (_, eventArgs) => { if (!rendering && eventArgs.Node?.Tag is TreeEntry entry) expandedProjects.Remove(entry.Key); };
        tree.ItemDrag += (_, eventArgs) =>
        {
            if (!busy && eventArgs.Item is TreeNode node && node.Tag is TreeEntry entry && CanMove(entry))
                tree.DoDragDrop(entry.Key, DragDropEffects.Move);
        };
        tree.DragOver += (_, eventArgs) => eventArgs.Effect = FindDropEntries(eventArgs) != null ? DragDropEffects.Move : DragDropEffects.None;
        tree.DragDrop += (_, eventArgs) =>
        {
            var entries = FindDropEntries(eventArgs);
            if (entries != null) MoveEntries(entries.Value.Source, entries.Value.Target);
        };
        changesList.DrawItem += DrawChange;
        KeyDown += async (_, eventArgs) =>
        {
            if (busy) return;
            if (eventArgs.Control && eventArgs.KeyCode == Keys.Enter) { eventArgs.SuppressKeyPress = true; await RunAsync(ApplyPreviewAsync); }
            if (eventArgs.Control && eventArgs.KeyCode == Keys.F) { eventArgs.SuppressKeyPress = true; search.Focus(); }
            if (eventArgs.KeyCode == Keys.Escape) { eventArgs.SuppressKeyPress = true; RestorePreview(); }
        };
        RegisterStaticCaptions();
        Shown += async (_, _) =>
        {
            await RunAsync(RefreshDataAsync);
            if (capturePath == null) return;
            PerformLayout();
            using var image = new Bitmap(Width, Height);
            DrawToBitmap(image, new Rectangle(Point.Empty, Size));
            image.Save(capturePath, ImageFormat.Png);
            Close();
        };
    }
}
