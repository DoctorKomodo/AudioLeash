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
    public void EvaluateDefaultChange_WhenSelectedDeviceUnavailable_ReturnsClearSelection()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");

        var result = state.EvaluateDefaultChange(
            newDefaultId: "device-B",
            isSelectedDeviceAvailable: false);

        Assert.Equal(RestoreDecision.ClearSelection, result);
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
    public void ClearSelection_NullsTheStoredId()
    {
        var state = new DeviceSelectionState();
        state.SelectDevice("device-A");
        state.ClearSelection();
        Assert.Null(state.SelectedDeviceId);
    }

    // ── IsInternalChange flag ────────────────────────────────────────────

    [Fact]
    public void IsInternalChange_DefaultsToFalse()
    {
        var state = new DeviceSelectionState();
        Assert.False(state.IsInternalChange);
    }
}
