#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// The settings window: per-flow priority lists (Playback / Recording) under flat underline
/// tabs, with "Start with Windows" and Save / Cancel in a footer band. Edits stay in the
/// supplied <see cref="PriorityListEditor"/>s; nothing is applied here. The caller applies
/// them when the form closes with <see cref="DialogResult.OK"/>.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string PlaybackHint  = "AudioLeash keeps the highest-ranked connected playback device as the default.";
    private const string RecordingHint = "AudioLeash keeps the highest-ranked connected recording device as the default.";
    private const string RankHint      = " Drag devices, or use ▲/▼, to rank them.";

    private readonly CheckBox _startWithWindows;
    private readonly bool     _initialStartWithWindows;
    private readonly Icon     _icon;

    public SettingsForm(PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows)
    {
        var theme = SettingsTheme.Current;
        _initialStartWithWindows = startWithWindows;

        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        ClientSize          = new Size(540, 480);
        MinimumSize         = new Size(440, 360);
        Text                = "AudioLeash Settings";
        StartPosition       = FormStartPosition.CenterScreen;
        MaximizeBox         = false;
        MinimizeBox         = false;
        ShowIcon            = true;
        BackColor           = theme.Window;
        ForeColor           = theme.Text;
        _icon               = TrayIconLoader.Load(SystemInformation.SmallIconSize);
        Icon                = _icon;

        // ── Content: tabs, hint, the selected flow's list ──────────────
        var tabs = new UnderlineTabStrip(theme) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        tabs.AddTab("Playback");
        tabs.AddTab("Recording");

        var hint = new Label
        {
            Text      = PlaybackHint + RankHint,
            ForeColor = theme.MutedText,
            AutoSize  = true,
            Dock      = DockStyle.Fill,
            Margin    = new Padding(0, 10, 0, 10),
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

        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 3,
            Padding     = new Padding(16, 8, 16, 16),
            BackColor   = theme.Window,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(tabs,  0, 0);
        content.Controls.Add(hint,  0, 1);
        content.Controls.Add(pages, 0, 2);

        // ── Footer: Start with Windows on the left, Save / Cancel on the right ──
        _startWithWindows = new CheckBox
        {
            Text      = "Start with Windows",
            Checked   = startWithWindows,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left,
            FlatStyle = FlatStyle.Flat,
            ForeColor = theme.Text,
            BackColor = theme.Footer,
        };
        _startWithWindows.FlatAppearance.BorderColor      = theme.MutedText;
        _startWithWindows.FlatAppearance.CheckedBackColor = theme.Footer;

        var save   = ThemedControls.StyleButton(new Button
        {
            Text = "Save", DialogResult = DialogResult.OK, AutoSize = true,
            MinimumSize = new Size(88, 30), Margin = new Padding(8, 0, 0, 0),
        }, theme, primary: true);
        var cancel = ThemedControls.StyleButton(new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true,
            MinimumSize = new Size(88, 30), Margin = new Padding(8, 0, 0, 0),
        }, theme);
        AcceptButton = save;
        CancelButton = cancel;

        // The form is shown modeless, and a button's DialogResult only ends a modal loop:
        // close explicitly. DialogResult (set by the button before Click) survives to FormClosed.
        save.Click   += (_, _) => Close();
        cancel.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize      = true,
            WrapContents  = false,
            Anchor        = AnchorStyles.Right,
            Margin        = new Padding(0),
            BackColor     = theme.Footer,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var footer = new TableLayoutPanel
        {
            Dock        = DockStyle.Bottom,
            AutoSize    = true,
            ColumnCount = 2,
            RowCount    = 1,
            Padding     = new Padding(16, 14, 16, 14),
            BackColor   = theme.Footer,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_startWithWindows, 0, 0);
        footer.Controls.Add(buttons, 1, 0);
        footer.Paint += (_, e) =>
        {
            using var line = new SolidBrush(theme.FooterBorder);
            e.Graphics.FillRectangle(line, 0, 0, footer.Width, 1);
        };

        Controls.Add(content);
        Controls.Add(footer);

        ResumeLayout(performLayout: false);
        PerformLayout();

        ActiveControl = playbackList;
    }

    /// <summary>Whether the "Start with Windows" box is checked.</summary>
    public bool StartWithWindows => _startWithWindows.Checked;

    /// <summary>
    /// Whether the user changed "Start with Windows" in this window. Save applies the option
    /// only then, so a change made from the tray while the window was open isn't reverted.
    /// </summary>
    public bool StartWithWindowsChanged => _startWithWindows.Checked != _initialStartWithWindows;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _icon.Dispose(); // after the window that shows it is gone
    }
}
