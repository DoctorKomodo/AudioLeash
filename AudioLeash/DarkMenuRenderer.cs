#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// A <see cref="ToolStripProfessionalRenderer"/> that paints the context menu with
/// dark-theme colours matching the Windows 10/11 dark mode palette.
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    // Shared by the renderer overrides and DarkColorTable (nested class can access these).
    private static readonly Color Background = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color Hover      = Color.FromArgb(0x3A, 0x3A, 0x3A);

    internal DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(0xE0, 0xE0, 0xE0)
            : Color.FromArgb(0x6E, 0x6E, 0x6E);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        // Do not call base: the default implementation repaints the image-margin column
        // and check-mark highlight using light-mode system colours, overwriting our dark fill.
        // DarkColorTable.CheckBackground / CheckSelectedBackground handle the checked-item
        // colour-table path; items with images are not used in this application.
        var rect  = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected && e.Item.Enabled ? Hover : Background;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        // Draw a single flat line spanning the full item width.  Unlike the standard
        // renderer (which indents by the image-margin width), the dark theme paints the
        // image-margin area the same background colour, so a full-width line is correct.
        int mid = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(0x44, 0x44, 0x44));
        e.Graphics.DrawLine(pen, 0, mid, e.Item.Width, mid);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
        base.OnRenderArrow(e);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Border = Color.FromArgb(0x44, 0x44, 0x44);

        public override Color MenuBorder                    => Border;
        public override Color MenuItemBorder                => Border;
        public override Color MenuItemSelected              => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd   => Hover;
        public override Color MenuItemPressedGradientBegin  => Color.FromArgb(0x30, 0x30, 0x30);
        public override Color MenuItemPressedGradientEnd    => Color.FromArgb(0x30, 0x30, 0x30);
        public override Color MenuStripGradientBegin        => Background;
        public override Color MenuStripGradientEnd          => Background;
        public override Color ToolStripDropDownBackground   => Background;
        public override Color ImageMarginGradientBegin      => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ImageMarginGradientMiddle     => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ImageMarginGradientEnd        => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color CheckBackground               => Color.FromArgb(0x30, 0x30, 0x30);
        public override Color CheckSelectedBackground       => Color.FromArgb(0x40, 0x40, 0x40);
        public override Color ButtonSelectedBorder          => Border;
        public override Color SeparatorDark                 => Border;
        public override Color SeparatorLight                => Border;
    }
}
