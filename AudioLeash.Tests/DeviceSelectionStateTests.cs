using AudioLeash;
using Xunit;

namespace AudioLeash.Tests;

public class DeviceSelectionStateTests
{
    // ── EvaluateDefaultChange ────────────────────────────────────────────

    [Fact]
    public void EvaluateDefaultChange_WhenNoDeviceSelected_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-A",
            isSelectedDeviceAvailable: true);
        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenIsInternalChange_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.IsInternalChange = true;

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenNewDefaultMatchesSelected_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-A",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenExternalChangeAndDeviceAvailable_ReturnsRestore()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: true);

        Assert.Equal(RestoreDecision.Restore, result);
    }

    [Fact]
    public void EvaluateDefaultChange_WhenSelectedDeviceUnavailable_ReturnsSuspend()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: false);

        Assert.Equal(RestoreDecision.Suspend, result);
    }

    // ── EvaluateDeviceStateChange ─────────────────────────────────────────

    [Fact]
    public void EvaluateDeviceStateChange_WhenNoSelection_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();

        var result = state.EvaluateDeviceStateChange("device-A", isNowActive: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDeviceStateChange_WhenDifferentDevice_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDeviceStateChange("device-B", isNowActive: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    [Fact]
    public void EvaluateDeviceStateChange_WhenSelectedDeviceBecomesActive_ReturnsRestore()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);

        var result = state.EvaluateDeviceStateChange("device-A", isNowActive: true);

        Assert.Equal(RestoreDecision.Restore, result);
        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateDeviceStateChange_WhenSelectedDeviceBecomesInactive_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDeviceStateChange("device-A", isNowActive: false);

        Assert.Equal(RestoreDecision.NoAction, result);
        Assert.False(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateDeviceStateChange_WhenIsInternalChange_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);
        state.IsInternalChange = true;

        var result = state.EvaluateDeviceStateChange("device-A", isNowActive: true);

        Assert.Equal(RestoreDecision.NoAction, result);
        // Availability still updated even during internal change
        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateDeviceStateChange_WhenAlreadyActive_ReturnsNoAction()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        // IsDeviceAvailable defaults to true via SelectDevice

        var result = state.EvaluateDeviceStateChange("device-A", isNowActive: true);

        Assert.Equal(RestoreDecision.NoAction, result);
    }

    // ── SelectDevice / ClearSelection ────────────────────────────────────

    [Fact]
    public void SelectDevice_StoresProvidedId()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        Assert.Equal("device-A", state.SelectedDeviceId);
    }

    [Fact]
    public void SelectDevice_SetsIsDeviceAvailableToTrue()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);

        state.SelectDevice("device-B");

        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void ClearSelection_NullsTheStoredId()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.ClearSelection();
        Assert.Null(state.SelectedDeviceId);
    }

    [Fact]
    public void ClearSelection_ResetsIsDeviceAvailableToTrue()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);

        state.ClearSelection();

        Assert.True(state.IsDeviceAvailable);
    }

    // ── IsInternalChange flag ────────────────────────────────────────────

    [Fact]
    public void IsInternalChange_DefaultsToFalse()
    {
        var state = new DeviceSelectionState();
        Assert.False(state.IsInternalChange);
    }

    // ── IsDeviceAvailable ────────────────────────────────────────────────

    [Fact]
    public void IsDeviceAvailable_DefaultsToTrue()
    {
        var state = new DeviceSelectionState();
        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void SetDeviceAvailability_UpdatesState()
    {
        var state = new DeviceSelectionState();
        state.SetDeviceAvailability(false);
        Assert.False(state.IsDeviceAvailable);

        state.SetDeviceAvailability(true);
        Assert.True(state.IsDeviceAvailable);
    }
}
