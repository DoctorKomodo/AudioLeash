#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// Flat text tabs with an accent underline under the selected one (Windows 11 style).
/// Only draws the headers; the owner shows the page for <see cref="SelectedIndex"/>.
/// Keyboard: Left/Right (and Ctrl+Tab in the form) move between tabs.
/// </summary>
internal sealed class UnderlineTabStrip : Control
{
    private readonly List<string> _titles = new();
    private readonly SettingsTheme _theme;
    private int _selectedIndex;
    private int _hotIndex = -1;

    public UnderlineTabStrip(SettingsTheme theme)
    {
        _theme = theme;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop        = true;
        BackColor      = theme.Window;
        ForeColor      = theme.Text;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public event EventHandler? SelectedIndexChanged;

    public void AddTab(string title)
    {
        _titles.Add(title);
        Invalidate();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _titles.Count || value == _selectedIndex) return;
            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Gap => LogicalToDeviceUnits(24);

    /// <summary>The header rectangle of each tab, left to right.</summary>
    private List<Rectangle> TabBounds()
    {
        var result = new List<Rectangle>(_titles.Count);
        int x = 0;
        foreach (string title in _titles)
        {
            int width = TextRenderer.MeasureText(title, Font).Width;
            result.Add(new Rectangle(x, 0, width, Height));
            x += width + Gap;
        }
        return result;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Hairline under the whole strip; the selected tab's underline sits on top of it.
        using (var line = new SolidBrush(_theme.Border))
            g.FillRectangle(line, 0, Height - 1, Width, 1);

        var bounds = TabBounds();
        int underline = LogicalToDeviceUnits(3);
        for (int i = 0; i < bounds.Count; i++)
        {
            bool selected = i == _selectedIndex;
            Color color = selected || i == _hotIndex ? _theme.Text : _theme.MutedText;
            var textRect = new Rectangle(bounds[i].X, 0, bounds[i].Width, Height - underline);
            TextRenderer.DrawText(g, _titles[i], Font, textRect, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

            if (selected)
            {
                using var accent = new SolidBrush(_theme.Accent);
                g.FillRectangle(accent, bounds[i].X, Height - underline, bounds[i].Width, underline);

                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textRect, 3, -2), color, BackColor);
            }
        }
    }

    private int HitTest(Point p)
    {
        var bounds = TabBounds();
        for (int i = 0; i < bounds.Count; i++)
            if (bounds[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int hit = HitTest(e.Location);
        if (hit >= 0) SelectedIndex = hit;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        if (hit == _hotIndex) return;
        _hotIndex = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hotIndex = -1;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left)  SelectedIndex = Math.Max(0, _selectedIndex - 1);
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_titles.Count - 1, _selectedIndex + 1);
    }

    protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
