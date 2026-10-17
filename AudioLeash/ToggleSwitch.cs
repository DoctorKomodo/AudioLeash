#nullable enable
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// A Windows 11-style on/off switch: a pill track with a round thumb. Click or Space toggles it.
/// Screen readers see a check button with the checked state.
/// </summary>
internal sealed class ToggleSwitch : Control
{
    private readonly SettingsTheme _theme;
    private bool _checked;
    private bool _hot;

    public ToggleSwitch(SettingsTheme theme)
    {
        _theme = theme;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable
               | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor  = Cursors.Hand;
        Size    = new Size(40, 20);
    }

    /// <summary>Raised when the user (not code) flips the switch.</summary>
    public event EventHandler? Toggled;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
    }

    private void Flip()
    {
        Checked = !Checked;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = new GraphicsPath();
        path.AddArc(track.Left, track.Top, track.Height, track.Height, 90, 180);
        path.AddArc(track.Right - track.Height, track.Top, track.Height, track.Height, 270, 180);
        path.CloseFigure();

        // On: accent fill with a dark thumb. Off: outlined track with a muted thumb.
        float inset = Height * (_hot ? 0.18f : 0.22f);
        float thumb = track.Height - 2 * inset;
        float thumbX = _checked ? track.Right - inset - thumb : track.Left + inset;
        var thumbRect = new RectangleF(thumbX, track.Top + inset, thumb, thumb);

        if (_checked)
        {
            using var fill = new SolidBrush(_theme.Accent);
            g.FillPath(fill, path);
            using var knob = new SolidBrush(_theme.AccentText);
            g.FillEllipse(knob, thumbRect);
        }
        else
        {
            using var pen = new Pen(_theme.MutedText, 1f);
            g.DrawPath(pen, path);
            using var knob = new SolidBrush(_theme.MutedText);
            g.FillEllipse(knob, thumbRect);
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(_theme.Text, 1f) { DashStyle = DashStyle.Dot };
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Flip();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.Space) return;
        Flip();
        e.Handled = true;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true;  Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e)   { base.OnGotFocus(e);   Invalidate(); }
    protected override void OnLostFocus(EventArgs e)  { base.OnLostFocus(e);  Invalidate(); }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    private sealed class SwitchAccessibleObject : ControlAccessibleObject
    {
        private readonly ToggleSwitch _owner;
        public SwitchAccessibleObject(ToggleSwitch owner) : base(owner) => _owner = owner;

        public override AccessibleRole Role => AccessibleRole.CheckButton;
        public override string DefaultAction => _owner.Checked ? "Turn off" : "Turn on";

        public override AccessibleStates State =>
            _owner.Checked ? base.State | AccessibleStates.Checked : base.State;

        public override void DoDefaultAction() => _owner.Flip();
    }
}
