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
/// Keyboard: Left/Right/Home/End on the strip; the owning form maps Ctrl+Tab and
/// Ctrl+PageUp/PageDown to <see cref="CycleSelection"/>. Exposes a tab list with one
/// tab per title to screen readers, like the classic <c>TabControl</c>.
/// </summary>
internal sealed class UnderlineTabStrip : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    private readonly List<string> _titles = new();
    private readonly SettingsTheme _theme;
    private int _selectedIndex;
    private int _hotIndex = -1;

    public UnderlineTabStrip(SettingsTheme theme)
    {
        _theme = theme;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop   = true;
        BackColor = theme.Window;
        ForeColor = theme.Text;
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
            AccessibilityNotifyClients(AccessibleEvents.Selection, value);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, value);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Selects the next (or previous) tab, wrapping around at either end.</summary>
    public void CycleSelection(bool forward)
    {
        if (_titles.Count == 0) return;
        int step = forward ? 1 : _titles.Count - 1;
        SelectedIndex = (_selectedIndex + step) % _titles.Count;
    }

    private int Gap        => LogicalToDeviceUnits(24);
    private int Underline  => LogicalToDeviceUnits(3);

    /// <summary>The header rectangle of each tab, left to right.</summary>
    private List<Rectangle> TabBounds()
    {
        var result = new List<Rectangle>(_titles.Count);
        int x = LogicalToDeviceUnits(2);
        foreach (string title in _titles)
        {
            int width = TextRenderer.MeasureText(title, Font, Size.Empty, TextFlags).Width;
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
        int underline = Underline;
        for (int i = 0; i < bounds.Count; i++)
        {
            bool selected = i == _selectedIndex;
            Color color = selected || i == _hotIndex ? _theme.Text : _theme.MutedText;
            var textRect = new Rectangle(bounds[i].X, 0, bounds[i].Width, Height - underline);
            TextRenderer.DrawText(g, _titles[i], Font, textRect, color, TextFlags | TextFormatFlags.VerticalCenter);

            if (selected)
            {
                using var accent = new SolidBrush(_theme.Accent);
                g.FillRectangle(accent, bounds[i].X, Height - underline, bounds[i].Width, underline);

                if (Focused && ShowFocusCues)
                {
                    int pad = LogicalToDeviceUnits(3);
                    var focus = Rectangle.Inflate(textRect, pad, -pad);
                    ControlPaint.DrawFocusRectangle(g, focus, color, BackColor);
                }
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
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_titles.Count == 0) return;

        // Left/Right follow reading order, so swap them in a right-to-left layout.
        bool rtl = RightToLeft == RightToLeft.Yes;
        int? target = e.KeyCode switch
        {
            Keys.Left  => _selectedIndex + (rtl ? 1 : -1),
            Keys.Right => _selectedIndex + (rtl ? -1 : 1),
            Keys.Home  => 0,
            Keys.End   => _titles.Count - 1,
            _          => null,
        };
        if (target is not { } index) return;

        SelectedIndex = Math.Clamp(index, 0, _titles.Count - 1);
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.Focus, _selectedIndex);
    }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    // ── Accessibility ───────────────────────────────────────────────────

    protected override AccessibleObject CreateAccessibilityInstance() => new StripAccessibleObject(this);

    /// <summary>The strip as a tab list whose children are the individual tabs.</summary>
    private sealed class StripAccessibleObject : ControlAccessibleObject
    {
        private readonly UnderlineTabStrip _strip;

        public StripAccessibleObject(UnderlineTabStrip strip) : base(strip) => _strip = strip;

        public override AccessibleRole Role => AccessibleRole.PageTabList;

        public override int GetChildCount() => _strip._titles.Count;

        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < _strip._titles.Count ? new TabAccessibleObject(_strip, index) : null;

        public override AccessibleObject? GetFocused() =>
            _strip.Focused ? GetChild(_strip._selectedIndex) : null;

        public override AccessibleObject? GetSelected() => GetChild(_strip._selectedIndex);
    }

    /// <summary>One tab header.</summary>
    private sealed class TabAccessibleObject : AccessibleObject
    {
        private readonly UnderlineTabStrip _strip;
        private readonly int _index;

        public TabAccessibleObject(UnderlineTabStrip strip, int index)
        {
            _strip = strip;
            _index = index;
        }

        public override string? Name => _strip._titles[_index];
        public override AccessibleRole Role => AccessibleRole.PageTab;
        public override AccessibleObject Parent => _strip.AccessibilityObject;
        public override string DefaultAction => "Switch";

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (_index == _strip._selectedIndex)
                {
                    state |= AccessibleStates.Selected;
                    if (_strip.Focused) state |= AccessibleStates.Focused;
                }
                return state;
            }
        }

        public override Rectangle Bounds => _strip.RectangleToScreen(_strip.TabBounds()[_index]);

        public override void DoDefaultAction() => _strip.SelectedIndex = _index;

        public override void Select(AccessibleSelection flags)
        {
            if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) != 0)
                _strip.SelectedIndex = _index;
            if ((flags & AccessibleSelection.TakeFocus) != 0)
                _strip.Focus();
        }
    }
}
