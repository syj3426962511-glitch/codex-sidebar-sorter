using System.Drawing.Drawing2D;

namespace SidebarSorter;

internal static class UiStyle
{
    public static readonly Color Background = Color.FromArgb(242, 245, 251);
    public static readonly Color Text = Color.FromArgb(25, 40, 65);
    public static readonly Color Accent = Color.FromArgb(61, 91, 238);
    public static readonly Color Border = Color.FromArgb(216, 225, 239);
    public static readonly Color Selected = Color.FromArgb(236, 241, 255);
    public static readonly Font Chinese = new("SimSun", 12);
    public static readonly Font English = new("Times New Roman", 12);
    public static readonly Font ChineseBold = new("SimSun", 12, FontStyle.Bold);
    public static readonly Font EnglishBold = new("Times New Roman", 12, FontStyle.Bold);

    public static void DrawText(Graphics graphics, string text, Rectangle bounds, Color color, bool bold = false)
    {
        int left = bounds.Left;
        for (int start = 0; start < text.Length;)
        {
            bool ascii = text[start] <= 127;
            int end = start + 1;
            while (end < text.Length && (text[end] <= 127) == ascii) end++;
            Font font = ascii ? (bold ? EnglishBold : English) : (bold ? ChineseBold : Chinese);
            string run = text[start..end];
            Size size = TextRenderer.MeasureText(graphics, run, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(graphics, run, font, new Rectangle(left, bounds.Top, Math.Max(0, bounds.Right - left), bounds.Height),
                color, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            left += size.Width;
            if (left >= bounds.Right) break;
            start = end;
        }
    }

    public static void ConfigureButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : Text;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }
}

internal sealed class ViewTab : Button
{
    private bool selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    public ViewTab()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        Size = new Size(130, 40);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Selected ? Color.White : UiStyle.Background);
        if (Selected)
        {
            using var pen = new Pen(UiStyle.Border);
            eventArgs.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        Rectangle bounds = ClientRectangle;
        bounds.Inflate(-16, 0);
        UiStyle.DrawText(eventArgs.Graphics, Text, bounds, Selected ? UiStyle.Accent : UiStyle.Text, Selected);
        if (Focused) ControlPaint.DrawFocusRectangle(eventArgs.Graphics, ClientRectangle);
    }
}

internal sealed class MixedLabel : Control
{
    protected override void OnTextChanged(EventArgs eventArgs) { base.OnTextChanged(eventArgs); Invalidate(); }
    protected override void OnPaint(PaintEventArgs eventArgs) => UiStyle.DrawText(eventArgs.Graphics, Text, ClientRectangle, ForeColor);
}

internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        Padding = new Padding(22);
        BackColor = Color.White;
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? UiStyle.Background);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        bounds.Inflate(-1, -1);
        using var path = new GraphicsPath();
        const int diameter = 28;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var background = new SolidBrush(Color.White);
        using var border = new Pen(UiStyle.Border);
        eventArgs.Graphics.FillPath(background, path);
        eventArgs.Graphics.DrawPath(border, path);
    }
}
