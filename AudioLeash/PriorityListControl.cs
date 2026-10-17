#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// One flow's priority list in the settings window: an owner-drawn list of the ranked
/// devices, a non-selectable divider, then the unranked devices. Rows are reordered with
/// the ▲/▼ buttons or by drag and drop; every edit goes through <see cref="PriorityListEditor"/>.
/// </summary>
internal sealed class PriorityListControl : UserControl
{
    internal const string DividerText = "Not ranked: AudioLeash won't switch to these on its own";

    /// <summary>Placeholder item for the divider row between ranked and unranked devices.</summary>
    private sealed class Divider
    {
        public static readonly Divider Instance = new();
        public override string ToString() => DividerText;
    }

    private readonly PriorityListEditor _editor;
    private readonly SettingsTheme _theme;
    private readonly ListBox _list;
    private readonly Button _upButton;
    private readonly Button _downButton;
    private Font _smallFont;

    private int _dividerIndex;
    private int _lastSelectedIndex = -1;
    private int _mouseDownIndex = -1;
    private Point _mouseDownPoint;
    private int _dropGap = -1; // insertion slot shown while dragging; -1 = none
    private string? _draggingId; // set only while a drag started in this list is in progress

    public PriorityListControl(PriorityListEditor editor, SettingsTheme theme)
    {
        _editor = editor;
        _theme  = theme;
        _smallFont = CreateSmallFont(Font);
        BackColor  = theme.Window;

        _list = new ListBox
        {
            Dock               = DockStyle.Fill,
            DrawMode           = DrawMode.OwnerDrawFixed,
            IntegralHeight     = false,
            AllowDrop          = true,
            SelectionMode      = SelectionMode.One,
            BorderStyle        = BorderStyle.None,
            BackColor          = theme.Surface,
            ForeColor          = theme.Text,
        };
        _list.DrawItem             += List_DrawItem;
        _list.SelectedIndexChanged += List_SelectedIndexChanged;
        _list.MouseDown            += List_MouseDown;
        _list.MouseMove            += List_MouseMove;
        _list.MouseUp              += (_, _) => _mouseDownIndex = -1;
        _list.DragOver             += List_DragOver;
        _list.DragDrop             += List_DragDrop;
        _list.DragLeave            += (_, _) => SetDropGap(-1);
        _list.Resize               += (_, _) => _list.Invalidate();

        _upButton   = ThemedControls.StyleButton(
            new Button { Text = "▲", Width = 36, Height = 32, AccessibleName = "Move up" }, theme);
        _downButton = ThemedControls.StyleButton(
            new Button { Text = "▼", Width = 36, Height = 32, AccessibleName = "Move down" }, theme);
        _upButton.Click   += (_, _) => MoveSelected(up: true);
        _downButton.Click += (_, _) => MoveSelected(up: false);

        var buttons = new FlowLayoutPanel
        {
            Dock          = DockStyle.Right,
            FlowDirection = FlowDirection.TopDown,
            AutoSize      = true,
            WrapContents  = false,
            Padding       = new Padding(8, 0, 0, 0),
            BackColor     = theme.Window,
        };
        buttons.Controls.Add(_upButton);
        buttons.Controls.Add(_downButton);

        // A one-pixel themed frame around the borderless list (the ListBox's own 3D/fixed
        // border ignores dark mode), with a little inner padding so rows don't touch it.
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = theme.Border };
        var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = theme.Surface };
        inner.Controls.Add(_list);
        frame.Controls.Add(inner);

        Controls.Add(frame);
        Controls.Add(buttons);

        UpdateItemHeight();
        Rebuild(selectId: null);
    }

    // ── Editing ─────────────────────────────────────────────────────────

    private string? SelectedId => (_list.SelectedItem as DeviceRow)?.Id;

    private void MoveSelected(bool up)
    {
        if (SelectedId is not { } id) return;
        bool moved = up ? _editor.MoveUp(id) : _editor.MoveDown(id);
        if (moved) Rebuild(selectId: id);
        _list.Focus();
    }

    /// <summary>Re-creates the list items from the editor and re-selects <paramref name="selectId"/>.</summary>
    private void Rebuild(string? selectId)
    {
        var ranked   = _editor.Ranked;
        var unranked = _editor.Unranked;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in ranked) _list.Items.Add(row);
        _dividerIndex = _list.Items.Add(Divider.Instance);
        foreach (var row in unranked) _list.Items.Add(row);

        int select = -1;
        if (selectId is not null)
        {
            for (int i = 0; i < _list.Items.Count; i++)
            {
                if (_list.Items[i] is DeviceRow r && r.Id == selectId) { select = i; break; }
            }
        }
        _lastSelectedIndex = select;
        _list.SelectedIndex = select;
        _list.EndUpdate();

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        int index = _list.SelectedIndex;
        bool isDevice = _list.SelectedItem is DeviceRow;
        bool isRanked = isDevice && index < _dividerIndex;

        // ▲ ranks an unranked row or moves a ranked one up; ▼ moves a ranked row down or unranks it.
        _upButton.Enabled   = isDevice && index != 0;
        _downButton.Enabled = isRanked;
    }

    private void List_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // The divider is not selectable. A click on it keeps the previous selection; arrow
        // keys step over it in the direction of travel (or stay put at either end).
        if (_list.SelectedItem is Divider)
        {
            int target = _lastSelectedIndex;
            if (Control.MouseButtons == MouseButtons.None && _lastSelectedIndex >= 0)
            {
                int across = _lastSelectedIndex < _dividerIndex ? _dividerIndex + 1 : _dividerIndex - 1;
                if (across >= 0 && across < _list.Items.Count) target = across;
            }
            _list.SelectedIndex = target;
            return;
        }
        _lastSelectedIndex = _list.SelectedIndex;
        UpdateButtons();
    }

    // ── Drag and drop ───────────────────────────────────────────────────

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        int index = _list.IndexFromPoint(e.Location);
        _mouseDownIndex = e.Button == MouseButtons.Left
                       && index != ListBox.NoMatches
                       && _list.Items[index] is DeviceRow
            ? index
            : -1;
        _mouseDownPoint = e.Location;
    }

    private void List_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_mouseDownIndex < 0 || (e.Button & MouseButtons.Left) == 0) return;

        var drag = SystemInformation.DragSize;
        var start = new Rectangle(
            _mouseDownPoint.X - drag.Width / 2, _mouseDownPoint.Y - drag.Height / 2, drag.Width, drag.Height);
        if (start.Contains(e.Location)) return;

        if (_list.Items[_mouseDownIndex] is not DeviceRow row) return;
        _mouseDownIndex = -1;

        // DoDragDrop blocks until the drop; only drags started here are accepted (not text,
        // files, or a row dragged from the other flow's list).
        _draggingId = row.Id;
        try { _list.DoDragDrop(row.Id, DragDropEffects.Move); }
        finally
        {
            _draggingId = null;
            SetDropGap(-1);
        }
    }

    private void List_DragOver(object? sender, DragEventArgs e)
    {
        if (_draggingId is null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        e.Effect = DragDropEffects.Move;
        SetDropGap(GapFromScreenPoint(new Point(e.X, e.Y)));
    }

    private void List_DragDrop(object? sender, DragEventArgs e)
    {
        SetDropGap(-1);
        if (_draggingId is not { } id) return;

        _editor.DropAtGap(id, GapFromScreenPoint(new Point(e.X, e.Y)));
        Rebuild(selectId: id);
    }

    /// <summary>The insertion slot (see <see cref="PriorityListEditor.DropAtGap"/>) under a screen point.</summary>
    private int GapFromScreenPoint(Point screen)
    {
        var p = _list.PointToClient(screen);
        int index = _list.IndexFromPoint(p);
        if (index == ListBox.NoMatches) return _list.Items.Count;

        var rect = _list.GetItemRectangle(index);
        return p.Y < rect.Top + rect.Height / 2 ? index : index + 1;
    }

    private void SetDropGap(int gap)
    {
        if (_dropGap == gap) return;
        _dropGap = gap;
        _list.Invalidate();
    }

    // ── Drawing ─────────────────────────────────────────────────────────

    private static Font CreateSmallFont(Font baseFont) =>
        new(baseFont.FontFamily, baseFont.SizeInPoints * 0.85f, FontStyle.Regular, GraphicsUnit.Point);

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        var old = _smallFont;
        _smallFont = CreateSmallFont(Font);
        old.Dispose();
        UpdateItemHeight();
    }

    private void UpdateItemHeight() =>
        _list.ItemHeight = Math.Min(255, _list.Font.Height + _smallFont.Height + LogicalToDeviceUnits(10));

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var g = e.Graphics;
        var bounds = e.Bounds;

        if (_list.Items[e.Index] is DeviceRow row)
            DrawDeviceRow(e, row);
        else
            DrawDivider(g, bounds);

        DrawDropIndicator(g, e.Index, bounds);
    }

    private void DrawDeviceRow(DrawItemEventArgs e, DeviceRow row)
    {
        var g = e.Graphics;
        var bounds = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool isRanked = e.Index < _dividerIndex;

        using (var back = new SolidBrush(selected ? _theme.Selection : _theme.Surface))
            g.FillRectangle(back, bounds);
        if (selected)
        {
            // Windows 11 list selection: a short accent pill at the left edge.
            int pill = LogicalToDeviceUnits(3);
            int inset = bounds.Height / 4;
            using var accent = new SolidBrush(_theme.Accent);
            g.FillRectangle(accent, bounds.Left, bounds.Top + inset, pill, bounds.Height - 2 * inset);
        }
        Color fore  = _theme.Text;
        Color muted = _theme.MutedText;

        const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                    | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        int pad = LogicalToDeviceUnits(6);
        int x = bounds.Left + pad;

        // Rank number column (blank for unranked rows, so names stay aligned).
        int rankWidth = TextRenderer.MeasureText(g, "00.", _list.Font).Width;
        if (isRanked)
        {
            TextRenderer.DrawText(g, $"{e.Index + 1}.", _list.Font,
                new Rectangle(x, bounds.Top, rankWidth, bounds.Height), fore, Flags | TextFormatFlags.Right);
        }
        x += rankWidth + pad;

        // Connected dot.
        int dot = LogicalToDeviceUnits(8);
        using (var brush = new SolidBrush(row.IsConnected ? _theme.Connected : _theme.Disconnected))
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(brush, x, bounds.Top + (bounds.Height - dot) / 2, dot, dot);
            g.SmoothingMode = oldMode;
        }
        x += dot + pad;

        // Connection status on the right, ranked rows only.
        int right = bounds.Right - pad;
        if (isRanked)
        {
            string status = row.IsConnected ? "connected" : "not connected";
            int statusWidth = TextRenderer.MeasureText(g, "not connected", _smallFont).Width;
            TextRenderer.DrawText(g, status, _smallFont,
                new Rectangle(right - statusWidth, bounds.Top, statusWidth, bounds.Height), muted,
                Flags | TextFormatFlags.Right);
            right -= statusWidth + pad;
        }

        // Name, with the description as a smaller second line.
        int width = Math.Max(0, right - x);
        int nameHeight = _list.Font.Height;
        int descHeight = _smallFont.Height;
        bool hasDesc = !string.IsNullOrEmpty(row.Description);
        int top = bounds.Top + (bounds.Height - nameHeight - (hasDesc ? descHeight : 0)) / 2;

        TextRenderer.DrawText(g, row.Name, _list.Font,
            new Rectangle(x, top, width, nameHeight), fore, Flags);
        if (hasDesc)
        {
            TextRenderer.DrawText(g, row.Description, _smallFont,
                new Rectangle(x, top + nameHeight, width, descHeight), muted, Flags);
        }

        if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0)
            ControlPaint.DrawFocusRectangle(g, bounds, fore, selected ? _theme.Selection : _theme.Surface);
    }

    private void DrawDivider(Graphics g, Rectangle bounds)
    {
        using (var back = new SolidBrush(_list.BackColor))
            g.FillRectangle(back, bounds);

        int pad = LogicalToDeviceUnits(6);
        const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                    | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        var textRect = new Rectangle(bounds.Left + pad, bounds.Top, bounds.Width - 2 * pad, bounds.Height);
        int textWidth = Math.Min(textRect.Width, TextRenderer.MeasureText(g, DividerText, _smallFont).Width);
        TextRenderer.DrawText(g, DividerText, _smallFont, textRect, _theme.MutedText, Flags);

        // Rule to the right of the label.
        int lineStart = textRect.Left + textWidth + pad;
        if (lineStart < bounds.Right - pad)
        {
            int mid = bounds.Top + bounds.Height / 2;
            using var pen = new Pen(_theme.Border);
            g.DrawLine(pen, lineStart, mid, bounds.Right - pad, mid);
        }
    }

    /// <summary>Draws the insertion line for the current drop slot, if it borders this row.</summary>
    private void DrawDropIndicator(Graphics g, int index, Rectangle bounds)
    {
        int y;
        if (_dropGap == index) y = bounds.Top;
        else if (_dropGap == index + 1 && index == _list.Items.Count - 1) y = bounds.Bottom - 2;
        else return;

        using var brush = new SolidBrush(_theme.Accent);
        g.FillRectangle(brush, bounds.Left, y, bounds.Width, 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _smallFont.Dispose();
        base.Dispose(disposing);
    }
}
