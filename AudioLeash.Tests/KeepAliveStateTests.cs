using AudioLeash;

namespace AudioLeash.Tests;

public class KeepAliveStateTests
{
    private static RankedDevice D(string id) => new(id, id + " name");

    private static KeepAliveState Make(string? defaultId, params string[] enabled) =>
        new(enabled.Select(D).ToList()) { DefaultId = defaultId };

    [Fact]
    public void DesiredId_DefaultEnabledAndNoPause_IsDefault()
    {
        Assert.Equal("A", Make("A", "A").DesiredId);
    }

    [Fact]
    public void DesiredId_DefaultNotEnabled_IsNull()
    {
        Assert.Null(Make("B", "A").DesiredId);
    }

    [Fact]
    public void DesiredId_NoDefault_IsNull()
    {
        Assert.Null(Make(null, "A").DesiredId);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void DesiredId_AnyPauseFlag_IsNull(bool locked, bool displayOff, bool suspended)
    {
        var state = Make("A", "A");
        state.IsLocked = locked;
        state.IsDisplayOff = displayOff;
        state.IsSuspended = suspended;
        Assert.Null(state.DesiredId);
    }

    [Fact]
    public void DesiredId_AllPauseFlagsCleared_ResumesDefault()
    {
        var state = Make("A", "A");
        state.IsLocked = state.IsDisplayOff = state.IsSuspended = true;
        state.IsLocked = state.IsDisplayOff = state.IsSuspended = false;
        Assert.Equal("A", state.DesiredId);
    }

    [Fact]
    public void Plan_NothingRunningNothingWanted_DoesNothing()
    {
        Assert.Equal(new KeepAlivePlan(false, null), Make("B", "A").Plan(null));
    }

    [Fact]
    public void Plan_NothingRunning_StartsDesired()
    {
        Assert.Equal(new KeepAlivePlan(false, "A"), Make("A", "A").Plan(null));
    }

    [Fact]
    public void Plan_AlreadyRunningDesired_DoesNothing()
    {
        Assert.Equal(new KeepAlivePlan(false, null), Make("A", "A").Plan("A"));
    }

    [Fact]
    public void Plan_DefaultMovesToDisabledDevice_Stops()
    {
        Assert.Equal(new KeepAlivePlan(true, null), Make("B", "A").Plan("A"));
    }

    [Fact]
    public void Plan_DefaultMovesToOtherEnabledDevice_Restarts()
    {
        Assert.Equal(new KeepAlivePlan(true, "B"), Make("B", "A", "B").Plan("A"));
    }

    [Fact]
    public void Plan_LockedWhileRunning_Stops()
    {
        var state = Make("A", "A");
        state.IsLocked = true;
        Assert.Equal(new KeepAlivePlan(true, null), state.Plan("A"));
    }

    [Fact]
    public void SetEnabled_TogglingDefaultOff_PlanStops()
    {
        var state = Make("A", "A");
        Assert.True(state.SetEnabled(D("A"), false));
        Assert.False(state.IsEnabled("A"));
        Assert.Equal(new KeepAlivePlan(true, null), state.Plan("A"));
    }

    [Fact]
    public void SetEnabled_TogglingOn_EnablesAndPlanStarts()
    {
        var state = Make("A");
        Assert.True(state.SetEnabled(D("A"), true));
        Assert.Equal(new KeepAlivePlan(false, "A"), state.Plan(null));
    }

    [Fact]
    public void SetEnabled_Duplicate_ReturnsFalseAndKeepsOneEntry()
    {
        var state = Make("A", "A");
        Assert.False(state.SetEnabled(D("A"), true));
        Assert.Single(state.Devices);
    }

    [Fact]
    public void SetEnabled_DisablingUnknown_ReturnsFalse()
    {
        Assert.False(Make("A", "A").SetEnabled(D("Z"), false));
    }

    [Fact]
    public void Constructor_DropsDuplicateAndEmptyIds()
    {
        var state = new KeepAliveState([D("A"), D("A"), new RankedDevice("", "x")]);
        Assert.Single(state.Devices);
    }

    [Fact]
    public void ReplaceAll_KeepsTheGivenOrder()
    {
        var state = Make("B", "A", "C");
        state.ReplaceAll([D("B"), D("C")]);

        Assert.Equal(["B", "C"], state.Devices.Select(d => d.Id));
        Assert.Equal("B", state.DesiredId);
        Assert.False(state.IsEnabled("A"));
    }

    [Fact]
    public void ReplaceAll_DropsDuplicateAndEmptyIds()
    {
        var state = Make(null, "A");
        state.ReplaceAll([D("B"), D("B"), D("")]);

        Assert.Equal(["B"], state.Devices.Select(d => d.Id));
    }
}
