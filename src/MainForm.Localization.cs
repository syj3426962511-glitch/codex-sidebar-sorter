using System.Runtime.InteropServices;

namespace SidebarSorter;

internal sealed partial class MainForm
{
    private readonly ToolSettingsStore settingsStore = new();
    private ToolSettings settings = new();
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 176 };
    private readonly Dictionary<Control, string> staticCaptions = new();
    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> languageFonts = new();

    private void LoadToolPreferences()
    {
        settings = settingsStore.Load();
        Localization.SetLanguage(settings.Language);
    }

    private void AddLanguageControls(FlowLayoutPanel heading)
    {
        heading.Controls.Add(new Label
        {
            Text = Localization.Text("语言"), AutoSize = true,
            Margin = new Padding(28, 11, 8, 0)
        });
        language.Margin = new Padding(0, 6, 0, 0);
        language.Items.AddRange([
            new LanguageChoice("system", ""), new LanguageChoice("zh-CN", "中文"), new LanguageChoice("en", "English")
        ]);
        language.SelectedIndex = language.Items.Cast<LanguageChoice>().ToList().FindIndex(item => item.Code == settings.Language);
        heading.Controls.Add(language);
        language.SelectedIndexChanged += async (_, _) =>
        {
            if (updating || language.SelectedItem is not LanguageChoice choice) return;
            await RunAsync(() =>
            {
                settings = settings with { Language = choice.Code };
                settingsStore.Save(settings);
                string previousStatus = Localization.SourceText(status.Text);
                Localization.SetLanguage(choice.Code);
                ApplyLanguage();
                status.Text = Localization.Text(previousStatus);
                return Task.CompletedTask;
            });
        };
    }

    private void RegisterStaticCaptions()
    {
        Control[] dynamicControls = [summary, listTitle, pending, changeTitle, readCheck, planCheck, status,
            selectionInfo, quickSort, grouping, projectsTab, chatsTab, allTab];
        foreach (Control control in AllControls(this))
        {
            if (control.Text.Length == 0 || control is TextBoxBase or ListControl or TreeView || dynamicControls.Contains(control)) continue;
            string source = Localization.SourceText(control.Text);
            if (Localization.HasEnglish(source)) staticCaptions[control] = source;
        }
        Disposed += (_, _) =>
        {
            foreach (Font font in languageFonts.Values) font.Dispose();
        };
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        foreach (var caption in staticCaptions) caption.Key.Text = Localization.Text(caption.Value);
        foreach (Control control in AllControls(this))
        {
            string family = Localization.IsChinese ? "SimSun" : "Times New Roman";
            var key = (family, control.Font.SizeInPoints, control.Font.Style);
            if (!languageFonts.TryGetValue(key, out Font? font))
                languageFonts[key] = font = new Font(family, key.Item2, key.Item3);
            control.Font = font;
        }
        int projectsMode = projectOrder.SelectedIndex;
        int chatsMode = chatOrder.SelectedIndex;
        bool wasUpdating = updating;
        updating = true;
        projectOrder.Items.Clear();
        chatOrder.Items.Clear();
        projectOrder.Items.AddRange(SortModes);
        chatOrder.Items.AddRange(SortModes);
        projectOrder.SelectedIndex = projectsMode;
        chatOrder.SelectedIndex = chatsMode;
        updating = wasUpdating;
        scope.Refresh();
        language.Refresh();
        if (workspace != null) RenderPreview();
        Invalidate(true);
    }

    private static IEnumerable<Control> AllControls(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (Control control in AllControls(child)) yield return control;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        SetProp(Handle, "ChatSorterMainWindow", new IntPtr(1));
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        RemoveProp(Handle, "ChatSorterMainWindow");
        base.OnHandleDestroyed(eventArgs);
    }

    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr window, string name);
}
