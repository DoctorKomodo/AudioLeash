namespace AudioLeash;

internal enum RestoreDecision
{
    NoAction,
    Restore,
    ClearSelection,
}

/// <summary>
/// Pure state machine for the device selection and auto-restore logic.
/// Contains no WinForms or audio-stack dependencies — fully unit-testable.
/// </summary>
internal sealed class DeviceSelectionState
{
    /// <summary>ID of the device the user has explicitly selected.</summary>
    public string? SelectedDeviceId { get; private set; }

    /// <summary>
    /// Set to <c>true</c> while the app itself is switching the default device,
    /// to prevent the change-notification handler from triggering a feedback loop.
    /// </summary>
    public bool IsInternalChange { get; set; }

    public void SelectDevice(string deviceId) => SelectedDeviceId = deviceId;

    public void ClearSelection() => SelectedDeviceId = null;

    /// <summary>
    /// Decides what the app should do when Windows reports that the default
    /// playback device has changed to <paramref name="newDefaultId"/>.
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
        if (!isSelectedDeviceAvailable) return RestoreDecision.ClearSelection;

        return RestoreDecision.Restore;
    }
}
