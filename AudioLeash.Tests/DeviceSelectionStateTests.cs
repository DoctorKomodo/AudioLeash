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

    // ── EvaluateSettledState ──────────────────────────────────────────────

    [Fact]
    public void EvaluateSettledState_WhenNoSelection_ReturnsNone()
    {
        var state = new DeviceSelectionState();

        var result = state.EvaluateSettledState(isAvailableNow: true);

        Assert.Equal(SettledOutcome.None, result);
    }

    [Fact]
    public void EvaluateSettledState_WhenSelectedDeviceBecomesAvailable_ReturnsReconnected()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);

        var result = state.EvaluateSettledState(isAvailableNow: true);

        Assert.Equal(SettledOutcome.NotifyReconnected, result);
        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateSettledState_WhenSelectedDeviceBecomesUnavailable_ReturnsDisconnected()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        // IsDeviceAvailable defaults to true via SelectDevice

        var result = state.EvaluateSettledState(isAvailableNow: false);

        Assert.Equal(SettledOutcome.NotifyDisconnected, result);
        Assert.False(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateSettledState_WhenStillAvailable_ReturnsReassertSilently()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        // Was available, settled available (a flap that recovered)

        var result = state.EvaluateSettledState(isAvailableNow: true);

        Assert.Equal(SettledOutcome.ReassertSilently, result);
        Assert.True(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateSettledState_WhenStillUnavailable_ReturnsNone()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.SetDeviceAvailability(false);

        var result = state.EvaluateSettledState(isAvailableNow: false);

        Assert.Equal(SettledOutcome.None, result);
        Assert.False(state.IsDeviceAvailable);
    }

    [Fact]
    public void EvaluateSettledState_FlapThatRecovers_DoesNotReportDisconnectOrReconnect()
    {
        // Simulates a rapid disconnect/reconnect burst that is debounced to a single
        // settled evaluation: the device was available before and is available after,
        // so the net outcome must be silent (no disconnect/reconnect notifications).
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateSettledState(isAvailableNow: true);

        Assert.Equal(SettledOutcome.ReassertSilently, result);
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
