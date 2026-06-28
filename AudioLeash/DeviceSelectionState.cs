namespace AudioLeash;

internal enum RestoreDecision
{
    NoAction,
    Restore,
    Suspend,
}

/// <summary>
/// The action to take once a burst of device state changes has settled
/// (see <see cref="DeviceSelectionState.EvaluateSettledState"/>). A rapid
/// disconnect/reconnect flap collapses to a single outcome reflecting the
/// net change between the last-notified state and the settled state.
/// </summary>
internal enum SettledOutcome
{
    /// <summary>Net state unchanged and the device is still unavailable — do nothing.</summary>
    None,

    /// <summary>The device is now unavailable (was available) — notify the user once.</summary>
    NotifyDisconnected,

    /// <summary>The device is available again (was unavailable) — restore it and notify once.</summary>
    NotifyReconnected,

    /// <summary>
    /// Net state unchanged and the device is available — a flap that ended where it
    /// started. Silently re-assert the default if needed, with no notification.
    /// </summary>
    ReassertSilently,
}

/// <summary>
/// Pure state machine for the device selection and auto-restore logic.
/// Contains no WinForms or audio-stack dependencies — fully unit-testable.
/// </summary>
internal sealed class DeviceSelectionState
{
    /// <summary>ID of the device the user has explicitly selected.</summary>
    public string? SelectedDeviceId { get; private set; }

    private volatile bool _isInternalChange;

    /// <summary>
    /// Set to <c>true</c> while the app itself is switching the default device,
    /// to prevent the change-notification handler from triggering a feedback loop.
    /// Written from both threads: the UI thread (menu click and restore path) and the
    /// Windows audio thread (which sets it <c>true</c> before dispatching to the UI thread).
    /// </summary>
    public bool IsInternalChange
    {
        get => _isInternalChange;
        set => _isInternalChange = value;
    }

    private volatile bool _isDeviceAvailable = true;

    /// <summary>
    /// Whether the currently selected device is present and active.
    /// When <c>false</c>, enforcement is suspended until the device reconnects.
    /// </summary>
    public bool IsDeviceAvailable
    {
        get => _isDeviceAvailable;
        private set => _isDeviceAvailable = value;
    }

    public void SetDeviceAvailability(bool available) => IsDeviceAvailable = available;

    public void SelectDevice(string deviceId)
    {
        SelectedDeviceId = deviceId;
        IsDeviceAvailable = true;
    }

    public void ClearSelection()
    {
        SelectedDeviceId = null;
        IsDeviceAvailable = true;
    }

    /// <summary>
    /// Decides what the app should do when Windows reports that the default
    /// audio device (playback or capture) has changed to <paramref name="newDefaultId"/>.
    /// </summary>
    /// <param name="newDefaultId">The ID of the device Windows just made the default.</param>
    /// <param name="isSelectedDeviceAvailable">
    ///   Whether the user-selected device is currently present and active.
    /// </param>
    public RestoreDecision EvaluateDefaultChange(
        string newDefaultId,
        bool isSelectedDeviceAvailable)
    {
        if (IsInternalChange)          return RestoreDecision.NoAction;
        if (SelectedDeviceId is null)  return RestoreDecision.NoAction;
        if (newDefaultId == SelectedDeviceId) return RestoreDecision.NoAction;
        if (!isSelectedDeviceAvailable) return RestoreDecision.Suspend;

        return RestoreDecision.Restore;
    }

    /// <summary>
    /// Decides what to do once a burst of device state changes has settled.
    /// <para>
    /// Device connect/disconnect events are debounced by the caller: rather than
    /// reacting to each transition, the caller waits for the endpoint to stop
    /// flapping, then calls this once with the device's final availability. The
    /// outcome reflects the net change between the last state the user was notified
    /// about (<see cref="IsDeviceAvailable"/>) and <paramref name="isAvailableNow"/>.
    /// </para>
    /// <para>
    /// This commits <paramref name="isAvailableNow"/> as the new notified state, so
    /// a flap that ends where it started produces no disconnect/reconnect spam.
    /// </para>
    /// </summary>
    public SettledOutcome EvaluateSettledState(bool isAvailableNow)
    {
        if (SelectedDeviceId is null) return SettledOutcome.None;

        bool wasAvailable = IsDeviceAvailable;
        IsDeviceAvailable = isAvailableNow;

        return (wasAvailable, isAvailableNow) switch
        {
            (false, true)  => SettledOutcome.NotifyReconnected,
            (true, false)  => SettledOutcome.NotifyDisconnected,
            (true, true)   => SettledOutcome.ReassertSilently,
            (false, false) => SettledOutcome.None,
        };
    }
}
