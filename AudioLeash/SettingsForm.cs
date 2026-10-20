#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>Raised when the user flips a device's switch on the "Keep devices awake" page.</summary>
internal sealed class KeepAliveToggledEventArgs(DeviceRow device, bool enabled) : EventArgs
{
    public DeviceRow Device  { get; } = device;
    public bool      Enabled { get; } = enabled;
}

/// <summary>
/// The settings window: per-flow priority lists (Playback / Recording) under flat underline
/// tabs, then settings cards: "Keep devices awake", which opens its own page of per-device
/// switches, and "Start with Windows" with an on/off switch. There is no Save or Cancel: list
/// edits stay in the supplied <see cref="PriorityListEditor"/>s and the caller applies them when
/// the window closes (so rearranging never switches audio mid-edit), while the switches raise
/// <see cref="StartWithWindowsToggled"/> and <see cref="KeepAliveToggled"/> immediately — neither
/// changes the default device.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string PlaybackHint  = "AudioLeash keeps the highest-ranked connected playback device as the default.";
    private const string RecordingHint = "AudioLeash keeps the highest-ranked connected recording device as the default.";
    private const string RankHint      = " Drag devices, or use ▲/▼, to rank them. Changes apply when you close this window.";
    private const string KeepAliveHint =
        "Plays inaudible silence to these devices while they're the default, so receivers and TVs don't " +
        "cut off the start of sounds. Pauses while your PC is locked or the screen is off.";

    private readonly SettingsTheme     _theme;
    private readonly UnderlineTabStrip _tabs;
    private readonly ToggleSwitch      _startWithWindows;
    private readonly Icon              _icon;
    private readonly Font              _pageTitleFont;
    private readonly Font              _smallFont;
    private readonly TableLayoutPanel  _mainPage;
    private readonly TableLayoutPanel  _keepAlivePage;
    private readonly CardPanel         _keepAliveCard;
    private readonly Label             _keepAliveSummary;
    private readonly Button            _backButton;
    private readonly List<ToggleSwitch> _keepAliveSwitches = [];
    private int _keepAliveCount;

    /// <param name="keepAlive">Rows for the "Keep devices awake" page.</param>
    /// <param name="theme">Palette to draw with; defaults to the current Windows app theme.</param>
    public SettingsForm(
        PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows,
        IReadOnlyList<KeepAliveRow> keepAlive, SettingsTheme? theme = null)
    {
        theme ??= SettingsTheme.Current;
        _theme = theme;

        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        ClientSize          = new Size(540, 500);
        MinimumSize         = new Size(440, 380);
        Text                = "AudioLeash Settings";
        StartPosition       = FormStartPosition.CenterScreen;
        MaximizeBox         = false;
        MinimizeBox         = false;
        ShowIcon            = true;
        BackColor           = theme.Window;
        ForeColor           = theme.Text;
        _icon               = TrayIconLoader.Load(SystemInformation.SmallIconSize);
        Icon                = _icon;
        _pageTitleFont      = new Font(Font.FontFamily, Font.SizeInPoints * 1.4f, FontStyle.Regular, GraphicsUnit.Point);
        _smallFont          = new Font(Font.FontFamily, Font.SizeInPoints * 0.85f, FontStyle.Regular, GraphicsUnit.Point);

        // ── Tabs, hint, the selected flow's list ────────────────────────
        var tabs = _tabs = new UnderlineTabStrip(theme)
        {
            Dock = DockStyle.Fill, Margin = new Padding(0), AccessibleName = "Device type",
        };
        tabs.AddTab("Playback");
        tabs.AddTab("Recording");

        var hint = new Label
        {
            Text      = PlaybackHint + RankHint,
            ForeColor = theme.MutedText,
            AutoSize  = true,
            Dock      = DockStyle.Fill,
            Margin    = new Padding(0, 6, 0, 8),
        };

        var playbackList = new PriorityListControl(playback, theme) { Dock = DockStyle.Fill };
        var captureList  = new PriorityListControl(capture, theme)  { Dock = DockStyle.Fill, Visible = false };
        var pages = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = theme.Window };
        pages.Controls.Add(playbackList);
        pages.Controls.Add(captureList);

        tabs.SelectedIndexChanged += (_, _) =>
        {
            bool isPlayback = tabs.SelectedIndex == 0;
            playbackList.Visible = isPlayback;
            captureList.Visible  = !isPlayback;
            hint.Text = (isPlayback ? PlaybackHint : RecordingHint) + RankHint;
        };

        // ── "Keep devices awake" card: opens its own page ───────────────
        foreach (var row in keepAlive)
            if (row.Enabled) _keepAliveCount++;

        var chevron = new Label
        {
            Text = "›", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = theme.MutedText,
            BackColor = theme.Surface, Font = _pageTitleFont, Margin = new Padding(12, 0, 0, 0),
        };
        _keepAliveCard = CreateCard(
            "Keep devices awake", KeepAliveRows.Summary(_keepAliveCount), chevron,
            new Padding(0, 12, 0, 0), out _keepAliveSummary);
        _keepAliveCard.Selectable = true;
        _keepAliveCard.AccessibleName        = "Keep devices awake";
        _keepAliveCard.AccessibleDescription = "Choose devices to keep awake with inaudible silence";
        _keepAliveCard.Activated += (_, _) => ShowKeepAlivePage();
        WireCardClicks(_keepAliveCard, _keepAliveCard, ShowKeepAlivePage);

        // ── "Start with Windows" settings row (Windows 11 settings-card style) ──
        _startWithWindows = new ToggleSwitch(theme)
        {
            Checked        = startWithWindows,
            Anchor         = AnchorStyles.Right,
            BackColor      = theme.Surface,
            AccessibleName        = "Start with Windows",
            AccessibleDescription = "Launch AudioLeash when you sign in",
            Margin         = new Padding(12, 0, 0, 0),
        };
        _startWithWindows.Toggled += (_, _) =>
            StartWithWindowsToggled?.Invoke(this, EventArgs.Empty);

        var startupCard = CreateCard(
            "Start with Windows", "Launch AudioLeash when you sign in", _startWithWindows,
            new Padding(0, 8, 0, 0), out _);
        // Clicking anywhere on the row's text flips the switch, as in Windows Settings.
        WireCardClicks(startupCard, _startWithWindows, _startWithWindows.Toggle);

        var content = _mainPage = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(16, 8, 16, 16),
            BackColor   = theme.Window,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(tabs,           0, 0);
        content.Controls.Add(hint,           0, 1);
        content.Controls.Add(pages,          0, 2);
        content.Controls.Add(_keepAliveCard, 0, 3);
        content.Controls.Add(startupCard,    0, 4);

        // ── "Keep devices awake" page ────────────────────────────────────
        _backButton = ThemedControls.StyleButton(new Button
        {
            Text = "‹", Font = _pageTitleFont, Size = new Size(36, 36), Margin = new Padding(0, 0, 8, 0),
            AccessibleName = "Back", Anchor = AnchorStyles.Left,
        }, theme);
        _backButton.FlatAppearance.BorderSize = 0;
        _backButton.BackColor = theme.Window;
        _backButton.Click += (_, _) => ShowMainPage();

        var pageTitle = new Label
        {
            Text = "Keep devices awake", Font = _pageTitleFont, AutoSize = true, ForeColor = theme.Text,
            Anchor = AnchorStyles.Left, Margin = new Padding(0),
        };
        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false,
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), BackColor = theme.Window,
        };
        header.Controls.Add(_backButton);
        header.Controls.Add(pageTitle);

        var keepAliveHint = new Label
        {
            Text = KeepAliveHint, ForeColor = theme.MutedText, AutoSize = true, Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 8),
        };

        _keepAlivePage = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 3,
            Padding     = new Padding(16, 8, 16, 16),
            BackColor   = theme.Window,
            Visible     = false,
        };
        _keepAlivePage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _keepAlivePage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _keepAlivePage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _keepAlivePage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _keepAlivePage.Controls.Add(header,                       0, 0);
        _keepAlivePage.Controls.Add(keepAliveHint,                0, 1);
        _keepAlivePage.Controls.Add(CreateKeepAliveList(keepAlive), 0, 2);

        Controls.Add(_keepAlivePage);
        Controls.Add(content);

        ResumeLayout(performLayout: false);
        PerformLayout();

        ActiveControl = playbackList;
    }

    /// <summary>Raised as soon as the user flips "Start with Windows".</summary>
    public event EventHandler? StartWithWindowsToggled;

    /// <summary>Raised as soon as the user flips a device's switch on the "Keep devices awake" page.</summary>
    public event EventHandler<KeepAliveToggledEventArgs>? KeepAliveToggled;

    /// <summary>The switch's current position.</summary>
    public bool StartWithWindows => _startWithWindows.Checked;

    /// <summary>
    /// Moves the switch without raising <see cref="StartWithWindowsToggled"/> — used to mirror a
    /// change made from the tray menu, or to revert a toggle that couldn't be applied.
    /// </summary>
    public void SetStartWithWindows(bool enabled) => _startWithWindows.Checked = enabled;

    // ── Settings cards ──────────────────────────────────────────────────

    /// <summary>A Windows 11 settings card: title and subtitle on the left, <paramref name="trailing"/> on the right.</summary>
    private CardPanel CreateCard(string title, string subtitle, Control trailing, Padding margin, out Label subtitleLabel)
    {
        var titleLabel = new Label
        {
            Text = title, AutoSize = true, ForeColor = _theme.Text, BackColor = _theme.Surface,
            Margin = new Padding(0),
        };
        subtitleLabel = new Label
        {
            Text = subtitle, AutoSize = true,
            ForeColor = _theme.MutedText, BackColor = _theme.Surface, Margin = new Padding(0, 2, 0, 0),
        };
        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
            Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = _theme.Surface,
        };
        text.Controls.Add(titleLabel);
        text.Controls.Add(subtitleLabel);

        var card = new CardPanel(_theme)
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(14, 10, 14, 10), Margin = margin,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.Controls.Add(text,     0, 0);
        card.Controls.Add(trailing, 1, 0);
        return card;
    }

    /// <summary>
    /// Makes a left click anywhere on <paramref name="root"/>'s non-focusable parts run
    /// <paramref name="activate"/>, focusing <paramref name="focusTarget"/> first.
    /// </summary>
    private static void WireCardClicks(Control root, Control focusTarget, Action activate)
    {
        void Wire(Control c)
        {
            if (c is ToggleSwitch) return; // the switch handles its own clicks
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => focusTarget.Focus();
            c.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) activate();
            };
            foreach (Control child in c.Controls) Wire(child);
        }
        Wire(root);
    }

    // ── "Keep devices awake" page ───────────────────────────────────────

    /// <summary>A scrollable list with one row (name, details, switch) per playback device.</summary>
    private Panel CreateKeepAliveList(IReadOnlyList<KeepAliveRow> rows)
    {
        var scroller = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = _theme.Surface, Margin = new Padding(0),
            Padding = new Padding(14, 6, 14, 6),
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = _theme.Surface, Margin = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        if (rows.Count == 0)
        {
            table.Controls.Add(new Label
            {
                Text = "No playback devices found.", AutoSize = true, ForeColor = _theme.MutedText,
                BackColor = _theme.Surface, Margin = new Padding(0, 6, 0, 6),
            }, 0, 0);
        }

        foreach (var row in rows)
        {
            var device = row.Device;
            string details = device.Description ?? "";
            if (!device.IsConnected)
                details = details.Length == 0 ? "not connected" : details + " · not connected";

            var name = new Label
            {
                Text = device.Name, AutoSize = true, ForeColor = _theme.Text, BackColor = _theme.Surface,
                Margin = new Padding(0),
            };
            var text = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6), BackColor = _theme.Surface,
            };
            text.Controls.Add(name);
            if (details.Length > 0)
            {
                text.Controls.Add(new Label
                {
                    Text = details, AutoSize = true, Font = _smallFont, ForeColor = _theme.MutedText,
                    BackColor = _theme.Surface, Margin = new Padding(0, 1, 0, 0),
                });
            }

            var toggle = new ToggleSwitch(_theme)
            {
                Checked = row.Enabled, Anchor = AnchorStyles.Right, BackColor = _theme.Surface,
                AccessibleName = device.Name, AccessibleDescription = "Keep this device awake",
                Margin = new Padding(12, 0, 0, 0),
            };
            toggle.Toggled += (_, _) =>
            {
                _keepAliveCount += toggle.Checked ? 1 : -1;
                _keepAliveSummary.Text = KeepAliveRows.Summary(_keepAliveCount);
                KeepAliveToggled?.Invoke(this, new KeepAliveToggledEventArgs(device, toggle.Checked));
            };
            _keepAliveSwitches.Add(toggle);

            // Clicking the row's text flips its switch, as on the Start with Windows card.
            WireCardClicks(text, toggle, toggle.Toggle);

            int r = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(text,   0, r);
            table.Controls.Add(toggle, 1, r);
        }

        scroller.Controls.Add(table);
        return scroller;
    }

    /// <summary>
    /// Swaps to the "Keep devices awake" page. Only visibility changes: pending priority edits
    /// stay in their editors and still apply when the window closes.
    /// </summary>
    private void ShowKeepAlivePage()
    {
        if (_keepAlivePage.Visible) return;
        _keepAlivePage.Visible = true;
        _mainPage.Visible = false;
        (_keepAliveSwitches.Count > 0 ? (Control)_keepAliveSwitches[0] : _backButton).Focus();
    }

    private void ShowMainPage()
    {
        if (_mainPage.Visible) return;
        _mainPage.Visible = true;
        _keepAlivePage.Visible = false;
        _keepAliveCard.Focus();
    }

    /// <summary>
    /// A settings card: a bordered surface panel. When <see cref="Selectable"/>, it takes focus,
    /// shows a focus outline, and Enter or Space raises <see cref="Activated"/> — screen readers
    /// see a button.
    /// </summary>
    private sealed class CardPanel : TableLayoutPanel
    {
        private readonly SettingsTheme _theme;

        public CardPanel(SettingsTheme theme)
        {
            _theme = theme;
            BackColor = theme.Surface;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public event EventHandler? Activated;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectable
        {
            get => TabStop;
            set { SetStyle(ControlStyles.Selectable, value); TabStop = value; }
        }

        protected override bool IsInputKey(Keys keyData) =>
            (Selectable && keyData is Keys.Enter or Keys.Space) || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Selectable && e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = true;
                Activated?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(_theme.Accent, 2);
                e.Graphics.DrawRectangle(focus, 1, 1, Width - 3, Height - 3);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new CardAccessibleObject(this);

        private sealed class CardAccessibleObject(CardPanel owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleRole Role =>
                owner.Selectable ? AccessibleRole.PushButton : base.Role;
            public override string? DefaultAction => owner.Selectable ? "Open" : base.DefaultAction;
            public override void DoDefaultAction()
            {
                if (owner.Selectable) owner.Activated?.Invoke(owner, EventArgs.Empty);
                else base.DoDefaultAction();
            }
        }
    }

    // ── Title bar ───────────────────────────────────────────────────────

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR            = 34;
    private const int DWMWA_CAPTION_COLOR           = 35;
    private const int DWMWA_TEXT_COLOR              = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// The title bar is drawn by Windows, not the form, so it doesn't follow the palette on its
    /// own. Colour it to match the window (Windows 11 22000+; older versions reject the colour
    /// attributes and keep the default title bar, which is fine).
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (SystemInformation.HighContrast) return; // leave the user's high-contrast colours alone

        bool dark = _theme.Window.GetBrightness() < 0.5f;
        SetDwmAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        SetDwmAttribute(DWMWA_CAPTION_COLOR, ColorTranslator.ToWin32(_theme.Window));
        SetDwmAttribute(DWMWA_TEXT_COLOR,    ColorTranslator.ToWin32(_theme.Text));
        SetDwmAttribute(DWMWA_BORDER_COLOR,  ColorTranslator.ToWin32(_theme.Border));
    }

    private void SetDwmAttribute(int attribute, int value) =>
        _ = DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int)); // best-effort; see above

    /// <summary>
    /// Esc closes the window (which applies the edits) — or, on the "Keep devices awake" page,
    /// goes back. Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / Ctrl+PageUp switch tabs, as in a
    /// TabControl; they do nothing on the "Keep devices awake" page, which has no tabs.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool onKeepAlivePage = _keepAlivePage.Visible;
        switch (keyData)
        {
            case Keys.Escape when onKeepAlivePage:
                ShowMainPage();
                return true;
            case Keys.Escape:
                Close();
                return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.PageDown:
                if (!onKeepAlivePage) _tabs.CycleSelection(forward: true);
                return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.Control | Keys.PageUp:
                if (!onKeepAlivePage) _tabs.CycleSelection(forward: false);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // After the window that shows them is gone.
            _icon.Dispose();
            _pageTitleFont.Dispose();
            _smallFont.Dispose();
        }
    }
}
