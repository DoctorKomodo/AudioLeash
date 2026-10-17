#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioLeash;

/// <summary>
/// Application context that manages the system tray icon and audio device switching.
/// Runs without a visible window entirely through the notification area.
/// </summary>
public sealed class AudioLeashContext : ApplicationContext
{
    private readonly NotifyIcon              _trayIcon;
    private readonly ContextMenuStrip        _contextMenu;
    private readonly MMDeviceEnumerator      _enumerator;
    private readonly PolicyConfigClient      _policyConfig;
    private readonly DeviceSelectionState    _selection;
    private readonly DeviceSelectionState    _captureSelection;
    private readonly PriorityState           _playbackPriority;
    private readonly PriorityState           _capturePriority;
    private readonly AudioNotificationClient _notificationClient;
    private readonly SettingsService         _settingsService;
    private readonly StartupService          _startupService;
    private readonly Font                    _sectionHeaderFont;
    private readonly System.Windows.Forms.Timer _renderDebounce;
    private readonly System.Windows.Forms.Timer _captureDebounce;
    private readonly System.Windows.Forms.Timer? _startupGraceTimer;
    private string?                          _savedPlaybackDeviceName;
    private string?                          _savedCaptureDeviceName;
    private SettingsForm?                    _settingsForm;

    /// <summary>
    /// How long the selected device's state must stay settled before AudioLeash
    /// reacts. Absorbs rapid disconnect/reconnect flapping (e.g. an HDMI/eARC
    /// handshake black-screen) so a burst collapses to a single notification.
    /// </summary>
    private const int DebounceMs = 1000;

    /// <summary>
    /// How long a persisted priority-mode override may be absent at startup before it is
    /// treated as disconnected. Audio endpoints can enumerate late after boot, so a missing
    /// device at startup isn't an immediate disconnect.
    /// </summary>
    private const int StartupGraceMs = 5000;

    public AudioLeashContext()
    {
        _enumerator   = new MMDeviceEnumerator();
        _policyConfig = new PolicyConfigClient();
        _selection        = new DeviceSelectionState();
        _captureSelection = new DeviceSelectionState();
        _playbackPriority = new PriorityState();
        _capturePriority  = new PriorityState();
        _contextMenu      = new ContextMenuStrip();
        _sectionHeaderFont = new Font(_contextMenu.Font, FontStyle.Bold);
        ApplyTheme();

        // Per-flow debounce timers. Each device state change re-arms the relevant
        // timer; only when it stops flapping for DebounceMs do we reconcile and
        // notify once. WinForms timers tick on the UI thread.
        _renderDebounce  = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _captureDebounce = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _renderDebounce.Tick  += (_, _) => { _renderDebounce.Stop();  ReconcileDeviceState(DataFlow.Render); };
        _captureDebounce.Tick += (_, _) => { _captureDebounce.Stop(); ReconcileDeviceState(DataFlow.Capture); };

        _trayIcon = new NotifyIcon
        {
            Icon             = TrayIconLoader.Load(SystemInformation.SmallIconSize),
            ContextMenuStrip = _contextMenu,
            Text             = "AudioLeash",
            Visible          = true,
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        // Force the ContextMenuStrip's HWND to be created now, before registering for
        // device-change notifications. Control.InvokeRequired returns false when
        // IsHandleCreated is false (regardless of which thread is calling), so SafeInvoke
        // would incorrectly run on the audio COM thread before the menu is first displayed.
        // Accessing .Handle forces handle creation and must happen before calling
        // RegisterEndpointNotificationCallback to guarantee "handle exists before any
        // notification can arrive".
        _ = _contextMenu.Handle;
        _contextMenu.Opening += (_, _) => RefreshDeviceList();

        // Subscribe to Windows theme changes so the menu renderer updates in real time
        // when the user toggles dark/light mode in Windows Settings.
        WindowsTheme.Changed += OnThemeChanged;

        // Register for device-change notifications.
        // IMPORTANT: _notificationClient must be a class field, not a local variable.
        // If the GC collects it while Windows holds a native COM pointer the process
        // will crash with ExecutionEngineException. (NAudio issue #849)
        _notificationClient = new AudioNotificationClient(OnDefaultDeviceChanged, OnDeviceStateChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);

        _settingsService = new SettingsService();
        _startupService  = new StartupService();

        // Restore saved device selections (playback and capture).
        string? savedPlaybackId = _settingsService.LoadSelectedPlaybackDeviceId();
        string? savedCaptureId  = _settingsService.LoadSelectedCaptureDeviceId();
        _savedPlaybackDeviceName = _settingsService.LoadSelectedPlaybackDeviceName();
        _savedCaptureDeviceName  = _settingsService.LoadSelectedCaptureDeviceName();
        bool isFirstRun = savedPlaybackId is null && !_settingsService.HasSettingsFile;

        // Each flow starts in priority mode (ranked list configured) or legacy mode.
        // Only legacy flows can report "Saved Device Unavailable"; a priority flow's
        // absent override is handled by the startup grace period instead.
        bool playbackUnavailable = StartFlow(
            DataFlow.Render, _settingsService.LoadPlaybackPriority(), savedPlaybackId, out bool playbackGrace);
        bool captureUnavailable = StartFlow(
            DataFlow.Capture, _settingsService.LoadCapturePriority(), savedCaptureId, out bool captureGrace);

        if (playbackGrace || captureGrace)
        {
            // One-shot: once the grace period ends, an override that still hasn't
            // appeared is treated as disconnected by the settled reconcile.
            var graceTimer = new System.Windows.Forms.Timer { Interval = StartupGraceMs };
            graceTimer.Tick += (_, _) =>
            {
                graceTimer.Stop();

                // Only the flows that began grace: legacy flows must not be touched.
                if (playbackGrace)
                {
                    _playbackPriority.EndStartupGrace();
                    ReconcileDeviceState(DataFlow.Render);
                }
                if (captureGrace)
                {
                    _capturePriority.EndStartupGrace();
                    ReconcileDeviceState(DataFlow.Capture);
                }
            };
            _startupGraceTimer = graceTimer;
            graceTimer.Start();
        }

        UpdateTrayTooltip();

        if (isFirstRun)
        {
            _trayIcon.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "Welcome to AudioLeash",
                tipText:  "Click the tray icon and select a device to enable auto-restore.",
                tipIcon:  ToolTipIcon.Info);
        }
        else if (playbackUnavailable || captureUnavailable)
        {
            string detail = (playbackUnavailable, captureUnavailable) switch
            {
                (true, true)   => "Your saved playback and recording devices are",
                (true, false)  => "Your saved playback device is",
                (false, true)  => "Your saved recording device is",
                _              => "Your saved device is", // unreachable
            };
            _trayIcon.ShowBalloonTip(
                timeout:  4000,
                tipTitle: "Saved Device Unavailable",
                tipText:  $"{detail} currently disconnected. AudioLeash will restore automatically when reconnected.",
                tipIcon:  ToolTipIcon.Info);
        }

        RefreshDeviceList();
    }

    /// <summary>
    /// Loads <paramref name="ranked"/> into the flow's <see cref="PriorityState"/> and starts
    /// the flow in the matching mode. Priority mode: a saved pick becomes the override (under
    /// a startup grace period), the target is committed from the connected devices, and the
    /// default is switched to it silently. Legacy mode: the saved pick is restored as today.
    /// </summary>
    /// <param name="beganGrace">True when this flow started a startup grace period.</param>
    /// <returns>True when a legacy saved device is currently unavailable.</returns>
    private bool StartFlow(
        DataFlow flow, IReadOnlyList<RankedDevice> ranked, string? savedId, out bool beganGrace)
    {
        beganGrace = false;
        var priority = PriorityFor(flow);
        priority.SetRanked(ranked);

        if (!priority.IsPriorityMode)
        {
            bool restored = TryRestoreSavedDevice(flow, SelectionFor(flow), savedId);
            return savedId is not null && !restored;
        }

        if (savedId is not null)
        {
            priority.StartOverride(savedId);
            priority.BeginStartupGrace();
            beganGrace = true;
        }
        priority.Initialize(GetActiveIds(flow));

        if (priority.TargetId is { } targetId)
        {
            try { SetDefaultIfNeeded(flow, priority, targetId); }
            catch (Exception) { /* transient COM failure; the next default change or reconcile re-asserts */ }
        }
        return false;
    }

    /// <summary>
    /// Attempts to restore a previously saved device selection for the given flow.
    /// Always selects the device (even if currently unavailable).
    /// Returns <c>true</c> if the device is currently active, <c>false</c> if unavailable.
    /// </summary>
    private bool TryRestoreSavedDevice(DataFlow flow, DeviceSelectionState selection, string? savedId)
    {
        if (savedId is null) return false;

        var active = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        bool available = active.Any(d => d.ID == savedId);
        foreach (var d in active) d.Dispose();

        selection.SelectDevice(savedId);
        selection.SetDeviceAvailability(available);
        return available;
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // WinForms only shows the context menu automatically on right-click;
        // invoke it manually for left-click via reflection.
        typeof(NotifyIcon)
            .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(_trayIcon, null);
    }

    /// <summary>
    /// Rebuilds the context menu with the current list of active playback and recording devices.
    /// Called on every menu open and after any device switch.
    /// </summary>
    private void RefreshDeviceList()
    {
        foreach (ToolStripItem item in _contextMenu.Items)
        {
            if (item is ToolStripMenuItem mi)
            {
                if (mi.Tag is (MMDevice d, DataFlow _))
                    d.Dispose();
                else if (mi.Tag is MMDevice d2)
                    d2.Dispose();
            }
        }
        _contextMenu.Items.Clear();

        // ── Playback section ────────────────────────────────────────────
        AddDeviceSection(DataFlow.Render, "Playback", _selection);

        // ── Recording section ───────────────────────────────────────────
        AddDeviceSection(DataFlow.Capture, "Recording", _captureSelection);

        // ── Actions ─────────────────────────────────────────────────────
        _contextMenu.Items.Add(new ToolStripSeparator());

        bool anyOverride = _playbackPriority.OverrideId is not null
                        || _capturePriority.OverrideId is not null;
        bool anyLegacy   = !_playbackPriority.IsPriorityMode || !_capturePriority.IsPriorityMode;

        if (anyOverride)
        {
            var returnItem = new ToolStripMenuItem("Return to priority order");
            returnItem.Click += ReturnToPriority_Click;
            _contextMenu.Items.Add(returnItem);
        }

        if (anyLegacy)
        {
            var clearItem = new ToolStripMenuItem("Clear Selection  (disable auto-restore)")
            {
                Enabled = (!_playbackPriority.IsPriorityMode && _selection.SelectedDeviceId is not null)
                       || (!_capturePriority.IsPriorityMode && _captureSelection.SelectedDeviceId is not null),
            };
            clearItem.Click += ClearSelection_Click;
            _contextMenu.Items.Add(clearItem);
        }

        if (anyOverride || anyLegacy)
            _contextMenu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += OpenSettings_Click;
        _contextMenu.Items.Add(settingsItem);

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _startupService.IsEnabled,
        };
        startupItem.Click += StartupItem_Click;
        _contextMenu.Items.Add(startupItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += Exit_Click;
        _contextMenu.Items.Add(exitItem);
    }

    private void AddDeviceSection(
        DataFlow flow,
        string sectionLabel,
        DeviceSelectionState selection)
    {
        // Bold section header (non-clickable)
        var header = new ToolStripMenuItem(sectionLabel)
        {
            Enabled = false,
            Font = _sectionHeaderFont,
        };
        _contextMenu.Items.Add(header);
        _contextMenu.Items.Add(new ToolStripSeparator());

        var devices = _enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .OrderBy(d => d.FriendlyName)
            .ToList();

        MMDevice? defaultDevice = null;
        try { defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); }
        catch (System.Runtime.InteropServices.COMException) { /* no default device for this flow */ }
        string? defaultId = defaultDevice?.ID;
        defaultDevice?.Dispose();

        var priority = PriorityFor(flow);
        if (priority.IsPriorityMode)
        {
            AddPriorityDevices(flow, priority, devices, defaultId);
            return;
        }

        if (devices.Count == 0 && !(selection.SelectedDeviceId is not null && !selection.IsDeviceAvailable))
        {
            _contextMenu.Items.Add(new ToolStripMenuItem("No devices available") { Enabled = false });
        }
        else
        {
            foreach (var device in devices)
            {
                bool isUserSelected   = selection.SelectedDeviceId == device.ID;
                bool isWindowsDefault = defaultId == device.ID;

                string label = device.FriendlyName;
                if (isWindowsDefault && !isUserSelected)
                    label += "  (Windows default)";

                var menuItem = new ToolStripMenuItem(label)
                {
                    Tag     = (device, flow),
                    Checked = isUserSelected,
                };
                menuItem.Click += DeviceMenuItem_Click;
                _contextMenu.Items.Add(menuItem);
            }
        }

        // Show unavailable selected device as a grayed-out, checked entry
        if (selection.SelectedDeviceId is not null && !selection.IsDeviceAvailable)
        {
            string? savedName = flow == DataFlow.Render
                ? _savedPlaybackDeviceName
                : _savedCaptureDeviceName;
            string unavailableName = GetDeviceName(selection.SelectedDeviceId)
                                     ?? savedName
                                     ?? "Unknown Device";
            var unavailableItem = new ToolStripMenuItem($"{unavailableName}  (unavailable)")
            {
                Checked = true,
                Enabled = false,
            };
            _contextMenu.Items.Add(unavailableItem);
        }
    }

    /// <summary>
    /// Adds the device lines for a priority-mode flow: rank and manual markers, a check on
    /// the enforced target or override, and a grayed entry for an override that isn't active
    /// (startup grace). The menu items take ownership of the <paramref name="devices"/> via their tags.
    /// </summary>
    private void AddPriorityDevices(
        DataFlow flow, PriorityState priority, List<MMDevice> devices, string? defaultId)
    {
        string? targetId   = priority.TargetId;
        string? overrideId = priority.OverrideId;

        bool showUnavailable = overrideId is not null && !devices.Any(d => d.ID == overrideId);

        if (devices.Count == 0 && !showUnavailable)
            _contextMenu.Items.Add(new ToolStripMenuItem("No devices available") { Enabled = false });

        foreach (var device in devices)
        {
            bool isTarget   = targetId == device.ID;
            bool isOverride = overrideId == device.ID;

            string label = device.FriendlyName;
            if (priority.RankOf(device.ID) is { } rank) label += $"  #{rank}";
            if (isOverride) label += "  (manual)";
            if (defaultId == device.ID && !isTarget) label += "  (Windows default)";

            var menuItem = new ToolStripMenuItem(label)
            {
                Tag     = (device, flow),
                Checked = isTarget || isOverride,
            };
            menuItem.Click += DeviceMenuItem_Click;
            _contextMenu.Items.Add(menuItem);
        }

        if (showUnavailable)
        {
            string unavailableName = (flow == DataFlow.Render ? _savedPlaybackDeviceName : _savedCaptureDeviceName)
                                     ?? "Unknown Device";
            _contextMenu.Items.Add(new ToolStripMenuItem($"{unavailableName}  (unavailable)")
            {
                Checked = true,
                Enabled = false,
            });
        }
    }

    private void DeviceMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem menuItem) return;
        if (menuItem.Tag is not (MMDevice device, DataFlow flow)) return;

        var priority = PriorityFor(flow);
        if (priority.IsPriorityMode)
        {
            PickPriorityDevice(flow, priority, device.ID, device.FriendlyName);
            return;
        }

        var selection = flow == DataFlow.Render ? _selection : _captureSelection;

        try
        {
            selection.IsInternalChange = true;

            string deviceId   = device.ID;
            string deviceName = device.FriendlyName;
            _policyConfig.SetDefaultEndpoint(deviceId);

            selection.SelectDevice(deviceId);

            if (flow == DataFlow.Render)
            {
                _savedPlaybackDeviceName = deviceName;
                _settingsService.SaveSelectedPlaybackDevice(deviceId, deviceName);
            }
            else
            {
                _savedCaptureDeviceName = deviceName;
                _settingsService.SaveSelectedCaptureDevice(deviceId, deviceName);
            }

            UpdateTrayTooltip();

            string flowLabel = flow == DataFlow.Render ? "output" : "input";
            _trayIcon.ShowBalloonTip(
                timeout:  2500,
                tipTitle: flow == DataFlow.Render ? "Audio Device Selected" : "Recording Device Selected",
                tipText:  $"Default {flowLabel}: {deviceName}\n\nAuto-restore is now active.",
                tipIcon:  ToolTipIcon.Info);

            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            ShowError($"Could not set device:\n{ex.Message}");
        }
        finally
        {
            selection.IsInternalChange = false;
        }
    }

    /// <summary>
    /// Priority-mode manual pick: the device becomes the override (and enforced target), the
    /// default is switched to it, and the pick is persisted so a restart keeps it.
    /// </summary>
    private void PickPriorityDevice(DataFlow flow, PriorityState priority, string deviceId, string deviceName)
    {
        string? previousOverride = priority.OverrideId;
        try
        {
            // Commit the override first so the default-change notification caused by the switch
            // below already matches the target; SetDefaultIfNeeded also raises IsInternalChange.
            priority.StartOverride(deviceId);
            SetDefaultIfNeeded(flow, priority, deviceId);
        }
        catch (Exception ex)
        {
            // The switch failed: undo the in-memory commit so nothing enforces a device the
            // user was told could not be set. Nothing was persisted.
            try
            {
                if (previousOverride is null) priority.ReturnToPriority(GetActiveIds(flow));
                else                          priority.StartOverride(previousOverride);
            }
            catch (Exception) { /* enumeration failed; the next reconcile re-derives the target */ }

            UpdateTrayTooltip();
            RefreshDeviceList();
            ShowError($"Could not set device:\n{ex.Message}");
            return;
        }

        try
        {
            if (flow == DataFlow.Render)
            {
                _savedPlaybackDeviceName = deviceName;
                _settingsService.SaveSelectedPlaybackDevice(deviceId, deviceName);
            }
            else
            {
                _savedCaptureDeviceName = deviceName;
                _settingsService.SaveSelectedCaptureDevice(deviceId, deviceName);
            }

            UpdateTrayTooltip();

            _trayIcon.ShowBalloonTip(
                timeout:  2500,
                tipTitle: "Manual Device Selected",
                tipText:  $"{deviceName}\n\nAudioLeash stays on it until you return to priority order or it disconnects.",
                tipIcon:  ToolTipIcon.Info);

            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            // The device did change; only persisting/refreshing failed.
            ShowError($"Device switched, but the choice could not be saved:\n{ex.Message}");
        }
    }

    /// <summary>Ends every priority-mode override and resumes the ranked order. No balloon.</summary>
    private void ReturnToPriority_Click(object? sender, EventArgs e)
    {
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var priority = PriorityFor(flow);
            if (!priority.IsPriorityMode || priority.OverrideId is null) continue;

            try
            {
                string? targetId = priority.ReturnToPriority(GetActiveIds(flow));

                if (flow == DataFlow.Render)
                {
                    _savedPlaybackDeviceName = null;
                    _settingsService.SaveSelectedPlaybackDevice(null, null);
                }
                else
                {
                    _savedCaptureDeviceName = null;
                    _settingsService.SaveSelectedCaptureDevice(null, null);
                }

                if (targetId is not null)
                    SetDefaultIfNeeded(flow, priority, targetId);
            }
            catch (Exception ex)
            {
                ShowError($"Could not return to priority order:\n{ex.Message}");
            }
        }

        UpdateTrayTooltip();
        RefreshDeviceList();
    }

    /// <summary>
    /// Opens the settings window (modeless), or brings the open one to the front.
    /// Its edits are applied only when it closes via Save.
    /// </summary>
    private void OpenSettings_Click(object? sender, EventArgs e)
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        PriorityListEditor playback, capture;
        try
        {
            playback = CreatePriorityEditor(DataFlow.Render);
            capture  = CreatePriorityEditor(DataFlow.Capture);
        }
        catch (Exception ex)
        {
            ShowError($"Could not open settings:\n{ex.Message}");
            return;
        }

        var form = new SettingsForm(playback, capture, _startupService.IsEnabled);
        form.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            if (form.DialogResult == DialogResult.OK)
                ApplySettings(form, playback, capture);
            form.Dispose();
        };
        _settingsForm = form;
        form.Show();
    }

    /// <summary>
    /// Builds the list editor for <paramref name="flow"/> from its ranked list and every
    /// endpoint Windows knows about (active, unplugged or disabled).
    /// </summary>
    private PriorityListEditor CreatePriorityEditor(DataFlow flow)
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged | DeviceState.Disabled)
            .ToList();
        try
        {
            var known = new List<DeviceRow>();
            foreach (var d in devices)
            {
                try
                {
                    known.Add(new DeviceRow(d.ID, d.FriendlyName, d.DeviceFriendlyName, d.State == DeviceState.Active));
                }
                catch (Exception)
                {
                    // One misbehaving endpoint must not stop the settings window opening.
                }
            }
            return new PriorityListEditor(PriorityFor(flow).Ranked, known);
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }

    /// <summary>
    /// Applies a saved settings window: each flow's new ranked list (switching between
    /// legacy and priority mode as needed), then the startup option.
    /// </summary>
    private void ApplySettings(SettingsForm form, PriorityListEditor playback, PriorityListEditor capture)
    {
        ApplyFlowSettings(DataFlow.Render, playback.ToRankedList());
        ApplyFlowSettings(DataFlow.Capture, capture.ToRankedList());

        if (StartupService.ShouldApplyFromSettings(
                form.StartWithWindowsChanged, form.StartWithWindows, _startupService.IsEnabled))
        {
            try
            {
                if (form.StartWithWindows)
                    _startupService.Enable(Environment.ProcessPath ?? Application.ExecutablePath);
                else
                    _startupService.Disable();
            }
            catch (Exception ex)
            {
                ShowError($"Could not update startup setting:\n{ex.Message}");
            }
        }

        UpdateTrayTooltip();
        RefreshDeviceList();
    }

    /// <summary>
    /// Applies one flow's saved ranked list. The persisted pick follows
    /// <see cref="PriorityState.PlanPersistedPick"/>: entering priority mode drops the legacy
    /// pick, staying keeps the override, and leaving pins the current default as the legacy pick.
    /// </summary>
    private void ApplyFlowSettings(DataFlow flow, IReadOnlyList<RankedDevice> newList)
    {
        var priority  = PriorityFor(flow);
        var selection = SelectionFor(flow);
        bool wasPriority = priority.IsPriorityMode;
        bool isPriority  = newList.Count > 0;

        try
        {
            string? pick = PriorityState.PlanPersistedPick(
                wasPriority, isPriority, selection.SelectedDeviceId, priority.OverrideId, GetCurrentDefaultId(flow));

            // A pick kept from before (legacy pick or override) may not resolve live right now.
            string? savedName = flow == DataFlow.Render ? _savedPlaybackDeviceName : _savedCaptureDeviceName;
            string? pickName  = pick is null ? null : GetDeviceName(pick) ?? savedName;

            // Enumerate before committing, so nothing can throw between SetRanked and the
            // re-evaluation that keeps TargetId consistent with the new list.
            IReadOnlySet<string> active = isPriority ? GetActiveIds(flow) : new HashSet<string>();

            // Persist the list and the pick in one write so a partial write can't leave them
            // inconsistent. SaveSettings is best-effort: a failed write is silent, and the
            // in-memory state below is applied regardless.
            if (flow == DataFlow.Render)
            {
                _settingsService.SavePlaybackFlow(newList, pick, pickName);
                _savedPlaybackDeviceName = pickName;
            }
            else
            {
                _settingsService.SaveCaptureFlow(newList, pick, pickName);
                _savedCaptureDeviceName = pickName;
            }

            if (isPriority)
            {
                // Entering or staying in priority mode. On entry there's no override (pick is
                // null); when staying, the existing override carries over untouched.
                selection.ClearSelection();
                priority.SetRanked(newList);
                string? defaultBefore = GetCurrentDefaultId(flow);
                var decision = priority.EvaluateSettled(active);

                // Editing the list isn't an automatic switch, so the outcome balloons would
                // misdescribe it; report only a default that actually changed.
                ApplyPriorityDecision(flow, decision, notify: false);

                string? defaultAfter = GetCurrentDefaultId(flow);
                if (defaultAfter is not null && defaultAfter != defaultBefore)
                {
                    string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";
                    _trayIcon.ShowBalloonTip(
                        timeout:  3000,
                        tipTitle: $"{flowLabel} Switched",
                        tipText:  $"Now using {ResolvePriorityName(priority, defaultAfter)} (priority order)",
                        tipIcon:  ToolTipIcon.Info);
                }
            }
            else
            {
                // Legacy mode: drop any override and target. When leaving priority mode, the
                // current default becomes the legacy pick; an existing legacy pick (staying in
                // legacy mode) keeps its selection and availability as they are.
                priority.SetRanked(newList);
                priority.ReturnToPriority(active);
                if (wasPriority && pick is not null) selection.SelectDevice(pick);
            }
        }
        catch (Exception ex)
        {
            string flowLabel = flow == DataFlow.Render ? "playback" : "recording";
            ShowError($"Could not apply {flowLabel} settings:\n{ex.Message}");
        }
    }

    private void ClearSelection_Click(object? sender, EventArgs e)
    {
        // Legacy flows only: a priority-mode flow is released via "Return to priority order".
        bool playbackLegacy = !_playbackPriority.IsPriorityMode;
        bool captureLegacy  = !_capturePriority.IsPriorityMode;
        if (playbackLegacy)
        {
            _selection.ClearSelection();
            _savedPlaybackDeviceName = null;
            _settingsService.SaveSelectedPlaybackDevice(null, null);
        }
        if (captureLegacy)
        {
            _captureSelection.ClearSelection();
            _savedCaptureDeviceName = null;
            _settingsService.SaveSelectedCaptureDevice(null, null);
        }
        UpdateTrayTooltip();

        _trayIcon.ShowBalloonTip(
            timeout:  2500,
            tipTitle: "Selection Cleared",
            tipText:  ClearSelectionMessage(playbackLegacy, captureLegacy),
            tipIcon:  ToolTipIcon.Info);

        RefreshDeviceList();
    }

    /// <summary>
    /// The "Selection Cleared" balloon text. Clear Selection only releases legacy flows, so in
    /// a mixed setup it names the released flow and says the other still follows its priority order.
    /// </summary>
    internal static string ClearSelectionMessage(bool playbackLegacy, bool captureLegacy) =>
        (playbackLegacy, captureLegacy) switch
        {
            (true, false) => "Playback auto-restore disabled. Recording still follows your priority order.",
            (false, true) => "Recording auto-restore disabled. Playback still follows your priority order.",
            _             => "Auto-restore disabled. Select a device to re-enable.",
        };

    private void StartupItem_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_startupService.IsEnabled)
                _startupService.Disable();
            else
                _startupService.Enable(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            ShowError($"Could not update startup setting:\n{ex.Message}");
        }

        RefreshDeviceList();
    }

    /// <summary>
    /// Called by <see cref="AudioNotificationClient"/> on a Windows audio thread
    /// when the system default device changes for either playback or capture.
    /// </summary>
    private void OnDefaultDeviceChanged(DataFlow flow, string newDefaultId)
    {
        var selection = SelectionFor(flow);
        var priority  = PriorityFor(flow);
        bool isPriorityMode = priority.IsPriorityMode;

        // Both evaluators snapshot their enforced device once, so the availability check
        // and decision.RestoreId always agree (no re-read of a field the UI thread may clear).
        var decision = isPriorityMode
            ? priority.EvaluateDefaultChange(newDefaultId, id => IsDeviceActive(flow, id))
            : selection.EvaluateDefaultChange(newDefaultId, id => IsDeviceActive(flow, id));
        string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";

        // The loop-prevention flag lives on whichever state object enforces this flow.
        void SetInternalChange(bool value)
        {
            if (isPriorityMode) priority.IsInternalChange = value;
            else                selection.IsInternalChange = value;
        }

        switch (decision.Decision)
        {
            case RestoreDecision.NoAction:
                return;

            case RestoreDecision.Suspend:
                // Device is unavailable — keep the selection but suspend enforcement.
                // Route through the debouncer rather than reacting immediately: during
                // an HDMI/eARC flap the default bounces away and back repeatedly, and we
                // want a single settled notification (or none if it recovers), not one
                // per bounce. The debounced reconcile will notify "Disconnected" only if
                // the device is still gone once flapping stops, and restore it otherwise.
                ArmDebounce(flow);
                break;

            case RestoreDecision.Restore when decision.RestoreId is { } restoreId:
                // Set the flag on the audio thread BEFORE dispatching to the UI thread.
                // This prevents a second OnDefaultDeviceChanged from triggering another
                // restore attempt during the roundtrip to the UI thread.
                SetInternalChange(true);

                // CPolicyConfigClient is an STA COM object with no registered proxy/stub.
                // Calling it from the audio COM thread (a different apartment) causes an
                // InvalidCastException. Marshal the call to the UI STA thread instead.
                // SafeInvoke uses Control.Invoke (synchronous), so the audio thread
                // blocks here until the UI thread finishes and resets the flag.
                // The outer try/catch ensures the flag is cleared even if SafeInvoke
                // itself throws (e.g. ObjectDisposedException during shutdown).
                try
                {
                    SafeInvoke(() =>
                    {
                        try
                        {
                            // restoreId was snapshotted on the audio thread; the user may have
                            // changed the enforced device since. Skip a stale restore.
                            string? enforcedId = isPriorityMode ? priority.TargetId : selection.SelectedDeviceId;
                            if (enforcedId != restoreId) return;

                            _policyConfig.SetDefaultEndpoint(restoreId);

                            // Keep the committed availability in sync with this immediate
                            // restore. The device is present (Restore only fires when it is),
                            // so if a debounce tick is pending it will now evaluate
                            // (available -> available) = ReassertSilently and won't fire a
                            // duplicate "Reconnected" balloon on top of this "Restored" one.
                            // Legacy-only: PriorityState tracks its target, not availability.
                            if (!isPriorityMode)
                                selection.SetDeviceAvailability(true);

                            var restoreDevices = _enumerator
                                .EnumerateAudioEndPoints(flow, DeviceState.Active)
                                .ToList();
                            string restoredName = restoreDevices
                                .FirstOrDefault(d => d.ID == restoreId)?.FriendlyName ?? restoreId;
                            foreach (var d in restoreDevices) d.Dispose();

                            UpdateTrayTooltip();

                            _trayIcon.ShowBalloonTip(
                                timeout:  3000,
                                tipTitle: $"{flowLabel} Restored",
                                tipText:  $"Switched back to: {restoredName}",
                                tipIcon:  ToolTipIcon.Info);
                            RefreshDeviceList();
                        }
                        catch (Exception ex)
                        {
                            _trayIcon.ShowBalloonTip(
                                timeout:  3000,
                                tipTitle: "Restore Failed",
                                tipText:  $"Could not restore {flowLabel.ToLower()}:\n{ex.Message}",
                                tipIcon:  ToolTipIcon.Error);
                        }
                        finally
                        {
                            SetInternalChange(false);
                        }
                    });
                }
                catch
                {
                    SetInternalChange(false);
                }
                break;
        }
    }

    /// <summary>
    /// Called by <see cref="AudioNotificationClient"/> on a Windows audio thread
    /// when a device's state changes (connected, disconnected, etc.).
    /// </summary>
    /// <remarks>
    /// We don't act on the transition directly. A single physical event (e.g. an
    /// HDMI/eARC handshake) can produce many rapid disconnect/reconnect transitions;
    /// reacting to each one floods the user with notifications. Instead we re-arm a
    /// per-flow debounce timer and let <see cref="ReconcileDeviceState"/> act once the
    /// endpoint has settled. The final state is re-queried at settle time, so the
    /// specific <paramref name="newState"/> here is irrelevant.
    /// </remarks>
    private void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (_selection.SelectedDeviceId == deviceId || IsPriorityRelevant(DataFlow.Render, deviceId))
            ArmDebounce(DataFlow.Render);
        if (_captureSelection.SelectedDeviceId == deviceId || IsPriorityRelevant(DataFlow.Capture, deviceId))
            ArmDebounce(DataFlow.Capture);
    }

    /// <summary>
    /// True when <paramref name="flow"/> is in priority mode and the device is ranked
    /// or is the current override, so its connect/disconnect must be reconciled.
    /// </summary>
    private bool IsPriorityRelevant(DataFlow flow, string deviceId)
    {
        var priority = PriorityFor(flow);
        return priority.IsPriorityMode && priority.IsRelevant(deviceId);
    }

    /// <summary>
    /// (Re)starts the debounce timer for the given flow. Marshals to the UI thread
    /// because WinForms timers must be started from the thread that owns them.
    /// </summary>
    private void ArmDebounce(DataFlow flow) =>
        SafeInvoke(() =>
        {
            var timer = flow == DataFlow.Render ? _renderDebounce : _captureDebounce;
            timer.Stop();
            timer.Start();
        });

    /// <summary>
    /// Runs on the UI thread once the selected device for <paramref name="flow"/> has
    /// stopped flapping. Re-queries the device's actual availability and emits at most
    /// one notification reflecting the net change since the last notified state.
    /// </summary>
    private void ReconcileDeviceState(DataFlow flow)
    {
        // Runs from a WinForms Timer.Tick; an unhandled throw here would surface on the
        // UI message loop as a ThreadException. Enumeration/default queries are COM calls
        // that can fail transiently, so guard the whole reconcile.
        try
        {
            ReconcileDeviceStateCore(flow);
        }
        catch (Exception) { /* transient COM/enumeration failure; next event re-arms */ }
    }

    private void ReconcileDeviceStateCore(DataFlow flow)
    {
        var priority = PriorityFor(flow);
        if (priority.IsPriorityMode)
        {
            ApplyPriorityDecision(flow, priority.EvaluateSettled(GetActiveIds(flow)), notify: true);
            return;
        }

        var selection = SelectionFor(flow);
        if (selection.SelectedDeviceId is null) return;

        string selectedId = selection.SelectedDeviceId;
        bool available = IsDeviceActive(flow, selectedId);
        var outcome = selection.EvaluateSettledState(available);
        string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";

        switch (outcome)
        {
            case SettledOutcome.NotifyDisconnected:
            {
                string? savedName = flow == DataFlow.Render
                    ? _savedPlaybackDeviceName
                    : _savedCaptureDeviceName;
                string displayName = GetDeviceName(selectedId) ?? savedName ?? flowLabel.ToLower();

                UpdateTrayTooltip();
                _trayIcon.ShowBalloonTip(
                    timeout:  3000,
                    tipTitle: $"{flowLabel} Disconnected",
                    tipText:  $"{displayName} is no longer available. Waiting for reconnection…",
                    tipIcon:  ToolTipIcon.Warning);
                RefreshDeviceList();
                break;
            }

            case SettledOutcome.NotifyReconnected:
                RestoreSelectedDevice(flow, selection, selectedId, notify: true);
                break;

            case SettledOutcome.ReassertSilently:
                RestoreSelectedDevice(flow, selection, selectedId, notify: false);
                break;

            case SettledOutcome.None:
                UpdateTrayTooltip();
                RefreshDeviceList();
                break;
        }
    }

    /// <summary>
    /// Re-asserts the selected device as the default for <paramref name="flow"/> (only
    /// if Windows isn't already on it, to avoid redundant churn) and optionally shows a
    /// "Reconnected" notification. Runs on the UI thread.
    /// </summary>
    private void RestoreSelectedDevice(
        DataFlow flow,
        DeviceSelectionState selection,
        string deviceId,
        bool notify)
    {
        string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";
        try
        {
            if (GetCurrentDefaultId(flow) != deviceId)
            {
                selection.IsInternalChange = true;
                try { _policyConfig.SetDefaultEndpoint(deviceId); }
                finally { selection.IsInternalChange = false; }
            }

            UpdateTrayTooltip();
            if (notify)
            {
                string displayName = GetDeviceName(deviceId) ?? deviceId;
                _trayIcon.ShowBalloonTip(
                    timeout:  3000,
                    tipTitle: $"{flowLabel} Reconnected",
                    tipText:  $"Switched back to: {displayName}",
                    tipIcon:  ToolTipIcon.Info);
            }
            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(
                timeout:  3000,
                tipTitle: "Restore Failed",
                tipText:  $"Could not restore {flowLabel.ToLower()}:\n{ex.Message}",
                tipIcon:  ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Acts on a priority-mode decision on the UI thread: persists an ended override,
    /// switches the default to <see cref="PriorityDecision.TargetId"/> when Windows isn't
    /// already on it, and optionally shows the balloon for the outcome.
    /// </summary>
    private void ApplyPriorityDecision(DataFlow flow, PriorityDecision decision, bool notify)
    {
        var priority = PriorityFor(flow);
        string flowLabel = flow == DataFlow.Render ? "Audio Device" : "Recording Device";

        bool overrideEnded = decision.Outcome == PriorityOutcome.OverrideEnded;

        // Resolve the previous device's name before the saved name is cleared below. An
        // ended override may be unranked, so its saved pick name is the last fallback.
        string? savedName = flow == DataFlow.Render ? _savedPlaybackDeviceName : _savedCaptureDeviceName;
        string prevName = decision.PreviousTargetId is null
            ? "device"
            : ResolvePriorityName(priority, decision.PreviousTargetId, overrideEnded ? savedName : null);

        if (overrideEnded)
        {
            // The override is over; a restart must not bring it back.
            if (flow == DataFlow.Render)
            {
                _savedPlaybackDeviceName = null;
                _settingsService.SaveSelectedPlaybackDevice(null, null);
            }
            else
            {
                _savedCaptureDeviceName = null;
                _settingsService.SaveSelectedCaptureDevice(null, null);
            }
        }

        try
        {
            if (decision.TargetId is not null)
                SetDefaultIfNeeded(flow, priority, decision.TargetId);

            if (notify)
                ShowPriorityBalloon(priority, decision, flowLabel, prevName);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(
                timeout:  3000,
                tipTitle: "Restore Failed",
                tipText:  $"Could not restore {flowLabel.ToLower()}:\n{ex.Message}",
                tipIcon:  ToolTipIcon.Error);
        }

        UpdateTrayTooltip();
        RefreshDeviceList();
    }

    /// <summary>Shows the balloon for a priority outcome; silent outcomes show nothing.</summary>
    private void ShowPriorityBalloon(
        PriorityState priority, PriorityDecision decision, string flowLabel, string prevName)
    {
        string? name = decision.TargetId is null
            ? null
            : ResolvePriorityName(priority, decision.TargetId);

        (string title, string text, ToolTipIcon icon)? balloon = decision.Outcome switch
        {
            PriorityOutcome.SwitchedUp when decision.PreviousTargetId is null =>
                ($"{flowLabel} Switched", $"Switched to {name}", ToolTipIcon.Info),
            PriorityOutcome.SwitchedUp =>
                ($"{flowLabel} Switched", $"Switched to {name} (higher priority)", ToolTipIcon.Info),
            PriorityOutcome.FellBack =>
                ($"{flowLabel} Disconnected", $"{prevName} disconnected. Switched to {name}", ToolTipIcon.Warning),
            PriorityOutcome.OverrideEnded when name is not null =>
                ($"{flowLabel} Disconnected", $"{prevName} disconnected. Back to priority order: {name}", ToolTipIcon.Warning),
            PriorityOutcome.OverrideEnded or PriorityOutcome.NoneConnected =>
                ($"{flowLabel} Disconnected", $"{prevName} disconnected. No ranked devices are connected.", ToolTipIcon.Warning),
            _ => null, // ReassertSilently / None
        };

        if (balloon is var (title, text, icon))
        {
            _trayIcon.ShowBalloonTip(timeout: 3000, tipTitle: title, tipText: text, tipIcon: icon);
        }
    }

    /// <summary>
    /// Display name for a priority-mode device: the live name, then the cached ranked-list
    /// name, then <paramref name="fallbackName"/>, then <c>"device"</c>.
    /// </summary>
    private string ResolvePriorityName(PriorityState priority, string deviceId, string? fallbackName = null) =>
        GetDeviceName(deviceId)
        ?? priority.Ranked.FirstOrDefault(r => r.Id == deviceId)?.Name
        ?? fallbackName
        ?? "device";

    /// <summary>
    /// Switches the default for <paramref name="flow"/> to <paramref name="targetId"/> only
    /// when Windows isn't already on it, under <see cref="PriorityState.IsInternalChange"/>
    /// so the resulting notification isn't treated as an external change. UI thread only.
    /// </summary>
    private void SetDefaultIfNeeded(DataFlow flow, PriorityState state, string targetId)
    {
        if (GetCurrentDefaultId(flow) == targetId) return;

        state.IsInternalChange = true;
        try { _policyConfig.SetDefaultEndpoint(targetId); }
        finally { state.IsInternalChange = false; }
    }

    /// <summary>The IDs of the flow's currently active endpoints. Disposes every enumerated device.</summary>
    private HashSet<string> GetActiveIds(DataFlow flow)
    {
        var active = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        var ids = new HashSet<string>(active.Select(d => d.ID));
        foreach (var d in active) d.Dispose();
        return ids;
    }

    private PriorityState PriorityFor(DataFlow flow) =>
        flow == DataFlow.Render ? _playbackPriority : _capturePriority;

    private DeviceSelectionState SelectionFor(DataFlow flow) =>
        flow == DataFlow.Render ? _selection : _captureSelection;

    /// <summary>Returns whether <paramref name="deviceId"/> is currently an active endpoint for the flow.</summary>
    private bool IsDeviceActive(DataFlow flow, string deviceId)
    {
        var active = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
        bool found = active.Any(d => d.ID == deviceId);
        foreach (var d in active) d.Dispose();
        return found;
    }

    /// <summary>Returns the ID of the current Multimedia default endpoint for the flow, or null if none.</summary>
    private string? GetCurrentDefaultId(DataFlow flow)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    private void SafeInvoke(Action action)
    {
        if (!_contextMenu.IsHandleCreated) return;
        try
        {
            if (_contextMenu.InvokeRequired)
                _contextMenu.Invoke(action);
            else
                action(); // Already on the UI thread; invoke directly.
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* handle destroyed mid-flight */ }
    }

    /// <summary>
    /// Applies a renderer to <see cref="_contextMenu"/> that matches the current Windows
    /// colour theme (dark or light).  Safe to call on any thread that owns the menu handle.
    /// </summary>
    private void ApplyTheme() =>
        _contextMenu.Renderer = WindowsTheme.IsDarkMode
            ? new DarkMenuRenderer()
            : new ToolStripProfessionalRenderer();

    private void OnThemeChanged(object? sender, EventArgs e) => SafeInvoke(ApplyTheme);

    private static void ShowError(string message) =>
        MessageBox.Show(message, "AudioLeash", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void UpdateTrayTooltip()
    {
        string? playbackName = FormatDeviceTooltip(DataFlow.Render);
        string? captureName  = FormatDeviceTooltip(DataFlow.Capture);

        string text;
        if (playbackName is null && captureName is null)
            text = "AudioLeash";
        else if (captureName is null)
            text = $"AudioLeash\nPlayback: {playbackName}";
        else if (playbackName is null)
            text = $"AudioLeash\nRecording: {captureName}";
        else
            text = $"AudioLeash\nPlayback: {playbackName}\nRecording: {captureName}";

        _trayIcon.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    private string? FormatDeviceTooltip(DataFlow flow)
    {
        var priority = PriorityFor(flow);
        if (priority.IsPriorityMode)
        {
            string? targetId = priority.TargetId;
            if (targetId is null)
            {
                // Only a persisted override that hasn't enumerated yet is worth showing.
                if (!priority.InStartupGrace) return null;
                string? graceName = flow == DataFlow.Render ? _savedPlaybackDeviceName : _savedCaptureDeviceName;
                return $"{graceName ?? "Unknown"} (waiting)";
            }

            string targetName = ResolvePriorityName(priority, targetId);
            return priority.OverrideId == targetId ? $"{targetName} (manual)" : targetName;
        }

        var selection = SelectionFor(flow);
        if (selection.SelectedDeviceId is null) return null;
        string? name = GetDeviceName(selection.SelectedDeviceId);
        if (name is null)
        {
            string? savedName = flow == DataFlow.Render
                ? _savedPlaybackDeviceName
                : _savedCaptureDeviceName;
            if (savedName is null) return "Unknown (waiting)";
            return $"{savedName} (waiting)";
        }
        return selection.IsDeviceAvailable ? name : $"{name} (waiting)";
    }

    private string? GetDeviceName(string? deviceId)
    {
        if (deviceId is null) return null;
        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            return device.FriendlyName;
        }
        catch (Exception) { return null; }
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WindowsTheme.Changed -= OnThemeChanged;
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);

            _renderDebounce.Stop();
            _captureDebounce.Stop();
            _renderDebounce.Dispose();
            _captureDebounce.Dispose();
            _startupGraceTimer?.Stop();
            _startupGraceTimer?.Dispose();

            foreach (ToolStripItem item in _contextMenu.Items)
            {
                if (item is ToolStripMenuItem mi)
                {
                    if (mi.Tag is (MMDevice d, DataFlow _))
                        d.Dispose();
                    else if (mi.Tag is MMDevice d2)
                        d2.Dispose();
                }
            }

            _settingsForm?.Dispose();
            _sectionHeaderFont.Dispose();
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _enumerator.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Receives Windows audio endpoint change notifications.
    /// Must be kept alive as a class field -- not a local variable -- to prevent
    /// the GC from collecting it while Windows holds a native COM pointer to it.
    /// (NAudio issue #849: ExecutionEngineException on GC collection)
    /// </summary>
    private sealed class AudioNotificationClient : IMMNotificationClient
    {
        private readonly Action<DataFlow, string> _onDefaultChanged;
        private readonly Action<string, DeviceState> _onDeviceStateChanged;

        internal AudioNotificationClient(
            Action<DataFlow, string> onDefaultChanged,
            Action<string, DeviceState> onDeviceStateChanged)
        {
            _onDefaultChanged = onDefaultChanged;
            _onDeviceStateChanged = onDeviceStateChanged;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Windows can pass a null device ID when the last device of a role is removed.
            // NAudio declares the parameter as non-nullable, but the underlying COM call
            // can produce null. Guard against this to avoid spurious restore attempts.
            if (defaultDeviceId is null) return;

            // Only react to Multimedia role changes for both Render (playback) and
            // Capture (recording). Windows fires this for Console, Multimedia, and
            // Communications separately -- filtering to Multimedia prevents triple-firing.
            if (role == Role.Multimedia && (flow == DataFlow.Render || flow == DataFlow.Capture))
                _onDefaultChanged(flow, defaultDeviceId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
            => _onDeviceStateChanged(deviceId, newState);

        // Required by IMMNotificationClient; AudioLeash does not act on these.
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
