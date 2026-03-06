namespace AudioLeash;

internal enum RestoreDecision
{
    NoAction,
    Restore,
    Suspend,
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
    /// Decides what the app should do when a device's state changes
    /// (connected/disconnected). Returns <see cref="RestoreDecision.Restore"/>
    /// if the selected device just became available again.
    /// </summary>
    public RestoreDecision EvaluateDeviceStateChange(string deviceId, bool isNowActive)
    {
        if (SelectedDeviceId is null)    return RestoreDecision.NoAction;
        if (deviceId != SelectedDeviceId) return RestoreDecision.NoAction;

        bool wasAvailable = IsDeviceAvailable;
        IsDeviceAvailable = isNowActive;

        if (isNowActive && !wasAvailable && !IsInternalChange)
            return RestoreDecision.Restore;

        return RestoreDecision.NoAction;
    }
}
