using System.Drawing.Drawing2D;

namespace SidebarSorter;

internal sealed class ConversationTree : TreeView
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FlatView { get; set; }

    public ConversationTree()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
        BorderStyle = BorderStyle.None;
        BackColor = Color.White;
        Font = UiStyle.Chinese;
        ItemHeight = 42;
        Indent = 26;
        ShowLines = false;
        ShowRootLines = false;
        ShowPlusMinus = false;
        FullRowSelect = true;
        HideSelection = false;
        ShowNodeToolTips = true;
        AllowDrop = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    public (int Count, int Time) Columns() => (Math.Max(180, ClientSize.Width - 300), Math.Max(290, ClientSize.Width - 154));

    protected override void OnDrawNode(DrawTreeNodeEventArgs eventArgs)
    {
        if (eventArgs.Node?.Tag is not TreeEntry entry) return;
        Graphics graphics = eventArgs.Graphics;
        int top = eventArgs.Bounds.Top;
        int height = ItemHeight;
        bool selected = eventArgs.Node == SelectedNode;
        Color background = selected ? UiStyle.Selected : entry.IsProject ? Color.FromArgb(249, 250, 254) : Color.White;
        using var fill = new SolidBrush(background);
        graphics.FillRectangle(fill, 0, top, ClientSize.Width, height);
        if (selected)
        {
            using var accent = new SolidBrush(UiStyle.Accent);
            graphics.FillRectangle(accent, 0, top + 4, 3, height - 8);
        }
        using var rule = new Pen(Color.FromArgb(235, 240, 247));
        graphics.DrawLine(rule, 0, top + height - 1, ClientSize.Width, top + height - 1);

        int indent = eventArgs.Node.Level * Indent;
        int glyph = 13 + indent;
        if (entry.IsProject && eventArgs.Node.Nodes.Count > 0)
        {
            int middle = top + height / 2;
            Point[] points = eventArgs.Node.IsExpanded
                ? [new(glyph - 4, middle - 2), new(glyph + 4, middle - 2), new(glyph, middle + 3)]
                : [new(glyph - 2, middle - 4), new(glyph - 2, middle + 4), new(glyph + 3, middle)];
            using var arrow = new SolidBrush(UiStyle.Text);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPolygon(arrow, points);
        }
        if (entry.Position > 0)
            UiStyle.DrawText(graphics, entry.Position.ToString(), new Rectangle(28 + indent, top, 38, height), selected ? UiStyle.Accent : UiStyle.Text);

        int nameLeft = 84 + indent;
        using var icon = new SolidBrush(entry.IsProject ? Color.FromArgb(118, 139, 171) : Color.FromArgb(126, 152, 195));
        if (entry.IsProject)
        {
            graphics.FillRectangle(icon, 65 + indent, top + 17, 12, 9);
            graphics.FillRectangle(icon, 65 + indent, top + 14, 6, 4);
        }
        else graphics.FillEllipse(icon, 68 + indent, top + 19, 5, 5);

        (int count, int time) = Columns();
        bool changed = entry.OriginalPosition > 0 && entry.Position != entry.OriginalPosition && count - nameLeft >= 190;
        int badgeWidth = changed ? 65 : 0;
        UiStyle.DrawText(graphics, entry.Row.Title, new Rectangle(nameLeft, top, Math.Max(40, count - nameLeft - 14 - badgeWidth), height), UiStyle.Text, entry.IsProject);
        if (changed)
            UiStyle.DrawText(graphics, Localization.Text("Was ") + entry.OriginalPosition, new Rectangle(count - 74, top, 68, height), UiStyle.Accent);

        string detail = "";
        if (entry.IsProject)
            detail = entry.Group?.IsLoaded == false ? Localization.Text("Pending") : (entry.Group?.Chats.Count ?? entry.ChildCount ?? 0) + Localization.Text(" chats (count)");
        else if (FlatView)
            detail = entry.Group?.Row.Title ?? (entry.IsLoose ? Localization.Text("Ungrouped") : "");
        else if (entry.Row.IsPinned)
            detail = Localization.Text("Pinned");
        else if (entry.Row.Status is "active" or "systemError")
            detail = entry.Row.Status == "active" ? Localization.Text("Running") : Localization.Text("Error");
        UiStyle.DrawText(graphics, detail, new Rectangle(count + 8, top, time - count - 14, height), UiStyle.Text);
        UiStyle.DrawText(graphics, FormatTime(entry.Row.UpdatedAt), new Rectangle(time + 8, top, ClientSize.Width - time - 12, height), UiStyle.Text);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        TreeNode? node = GetNodeAt(eventArgs.Location);
        if (node?.Tag is not TreeEntry entry) return;
        SelectedNode = node;
        int glyph = 13 + node.Level * Indent;
        if (entry.IsProject && node.Nodes.Count > 0 && Math.Abs(eventArgs.X - glyph) < 12)
            node.Toggle();
    }

    public static string FormatTime(long? value)
    {
        if (value == null) return "";
        DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToLocalTime();
        TimeSpan elapsed = DateTimeOffset.Now - time;
        if (elapsed.TotalMinutes < 1) return Localization.Text("Just now");
        if (elapsed.TotalHours < 1) return (int)elapsed.TotalMinutes + Localization.Text(" min ago");
        if (elapsed.TotalDays < 1) return (int)elapsed.TotalHours + Localization.Text(" h ago");
        if (elapsed.TotalDays < 7) return (int)elapsed.TotalDays + Localization.Text(" d ago");
        return time.ToString("yyyy-MM-dd");
    }
}
