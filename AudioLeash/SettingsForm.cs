#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// The settings window: per-flow priority lists (Playback / Recording) and general options.
/// Edits stay in the supplied <see cref="PriorityListEditor"/>s; nothing is applied here.
/// The caller applies them when the form closes with <see cref="DialogResult.OK"/>.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly CheckBox _startWithWindows;
    private readonly Icon     _icon;

    public SettingsForm(PriorityListEditor playback, PriorityListEditor capture, bool startWithWindows)
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        ClientSize          = new Size(520, 460);
        MinimumSize         = new Size(420, 340);
        Text                = "AudioLeash Settings";
        StartPosition       = FormStartPosition.CenterScreen;
        MaximizeBox         = false;
        MinimizeBox         = false;
        ShowIcon            = true;
        _icon               = TrayIconLoader.Load(SystemInformation.SmallIconSize);
        Icon                = _icon;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateListPage("Playback", playback,
            "AudioLeash keeps the highest-ranked connected playback device as the default."));
        tabs.TabPages.Add(CreateListPage("Recording", capture,
            "AudioLeash keeps the highest-ranked connected recording device as the default."));

        _startWithWindows = new CheckBox
        {
            Text     = "Start with Windows",
            Checked  = startWithWindows,
            AutoSize = true,
            Location = new Point(12, 12),
        };
        var general = new TabPage("General") { Padding = new Padding(8), UseVisualStyleBackColor = true };
        general.Controls.Add(_startWithWindows);
        tabs.TabPages.Add(general);

        var save   = new Button { Text = "Save",   DialogResult = DialogResult.OK,     AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
        AcceptButton = save;
        CancelButton = cancel;

        // The form is shown modeless, and a button's DialogResult only ends a modal loop:
        // close explicitly. DialogResult (set by the button before Click) survives to FormClosed.
        save.Click   += (_, _) => Close();
        cancel.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize      = true,
            Padding       = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        Controls.Add(tabs);
        Controls.Add(buttons);

        ResumeLayout(performLayout: false);
        PerformLayout();
    }

    /// <summary>Whether the "Start with Windows" box is checked.</summary>
    public bool StartWithWindows => _startWithWindows.Checked;

    private static TabPage CreateListPage(string title, PriorityListEditor editor, string hint)
    {
        var page = new TabPage(title) { Padding = new Padding(8), UseVisualStyleBackColor = true };

        var help = new Label
        {
            Text     = $"{hint} Drag devices, or use ▲/▼, to rank them.",
            Dock     = DockStyle.Top,
            AutoSize = false,
            Height   = 40,
        };
        var list = new PriorityListControl(editor) { Dock = DockStyle.Fill };

        page.Controls.Add(list);
        page.Controls.Add(help);
        return page;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _icon.Dispose(); // after the window that shows it is gone
    }
}
