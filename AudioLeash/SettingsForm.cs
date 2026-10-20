#nullable enable
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// The settings window: per-flow priority lists (Playback / Recording) under flat underline
/// tabs, and a "Start with Windows" settings row with an on/off switch. There is no Save or
/// Cancel: list edits stay in the supplied <see cref="PriorityListEditor"/>s and the caller
/// applies them when the window closes (so rearranging never switches audio mid-edit), while
/// the switch raises <see cref="StartWithWindowsToggled"/> immediately.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string PlaybackHint  = "AudioLeash keeps the highest-ranked connected playback device as the default.";
    private const string RecordingHint = "AudioLeash keeps the highest-ranked connected recording device as the default.";
    private const string RankHint      = " Drag devices, or use ▲/▼, to rank them. Changes apply when you close this window.";

    private readonly SettingsTheme     _theme;
    private readonly UnderlineTabStrip _tabs;
    private readonly ToggleSwitch      _startWithWindows;
    private readonly Icon              _icon;

    /// <param name="theme">Palette to draw with; defaults to the current Windows app theme.</param>
    public SettingsForm(
        PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows, SettingsTheme? theme = null)
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

        var startupTitle = new Label
        {
            Text = "Start with Windows", AutoSize = true, ForeColor = theme.Text, BackColor = theme.Surface,
            Margin = new Padding(0),
        };
        var startupSubtitle = new Label
        {
            Text = "Launch AudioLeash when you sign in", AutoSize = true,
            ForeColor = theme.MutedText, BackColor = theme.Surface, Margin = new Padding(0, 2, 0, 0),
        };
        var startupText = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
            Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = theme.Surface,
        };
        startupText.Controls.Add(startupTitle);
        startupText.Controls.Add(startupSubtitle);

        var startupCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 12, 0, 0),
            BackColor = theme.Surface,
        };
        startupCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        startupCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        startupCard.Controls.Add(startupText, 0, 0);
        startupCard.Controls.Add(_startWithWindows, 1, 0);
        startupCard.Paint += (_, e) =>
        {
            using var pen = new Pen(theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, startupCard.Width - 1, startupCard.Height - 1);
        };
        // Clicking anywhere on the row's text flips the switch, as in Windows Settings.
        foreach (Control c in new Control[] { startupCard, startupText, startupTitle, startupSubtitle })
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => _startWithWindows.Focus();
            c.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) _startWithWindows.Toggle();
            };
        }

        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 4,
            Padding     = new Padding(16, 8, 16, 16),
            BackColor   = theme.Window,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(tabs,        0, 0);
        content.Controls.Add(hint,        0, 1);
        content.Controls.Add(pages,       0, 2);
        content.Controls.Add(startupCard, 0, 3);

        Controls.Add(content);

        ResumeLayout(performLayout: false);
        PerformLayout();

        ActiveControl = playbackList;
    }

    /// <summary>Raised as soon as the user flips "Start with Windows".</summary>
    public event EventHandler? StartWithWindowsToggled;

    /// <summary>The switch's current position.</summary>
    public bool StartWithWindows => _startWithWindows.Checked;

    /// <summary>
    /// Moves the switch without raising <see cref="StartWithWindowsToggled"/> — used to mirror a
    /// change made from the tray menu, or to revert a toggle that couldn't be applied.
    /// </summary>
    public void SetStartWithWindows(bool enabled) => _startWithWindows.Checked = enabled;

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
    /// Esc closes the window (which applies the edits). Ctrl+Tab / Ctrl+Shift+Tab and
    /// Ctrl+PageDown / Ctrl+PageUp switch tabs, as in a TabControl.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.PageDown:
                _tabs.CycleSelection(forward: true);
                return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.Control | Keys.PageUp:
                _tabs.CycleSelection(forward: false);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _icon.Dispose(); // after the window that shows it is gone
    }
}
