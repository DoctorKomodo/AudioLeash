using System.Linq;
using AudioLeash;
using Xunit;

namespace AudioLeash.Tests;

public class PriorityStateTests
{
    private static PriorityState Make(params string[] ids)
    {
        var state = new PriorityState();
        state.SetRanked(ids.Select(i => new RankedDevice(i, i)).ToList());
        return state;
    }

    private static RankedDevice D(string id) => new(id, id + " name");

    private static HashSet<string> On(params string[] ids) => new(ids);

    [Fact]
    public void Initialize_NoOverride_TargetsTopConnected()
    {
        var s = Make("tv", "hp", "spk");
        s.Initialize(On("hp", "spk"));
        Assert.Equal("hp", s.TargetId);
    }

    [Fact]
    public void Initialize_NoRankedConnected_TargetIsNull()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("other"));
        Assert.Null(s.TargetId);
    }

    [Fact]
    public void EvaluateSettled_HigherRankedConnects_SwitchesUp()
    {
        var s = Make("tv", "hp", "spk");
        s.Initialize(On("hp"));
        var d = s.EvaluateSettled(On("tv", "hp"));
        Assert.Equal(new PriorityDecision(PriorityOutcome.SwitchedUp, "tv", "hp"), d);
    }

    [Fact]
    public void EvaluateSettled_TargetDisconnects_FallsBack()
    {
        var s = Make("tv", "hp", "spk");
        s.Initialize(On("tv", "hp"));
        var d = s.EvaluateSettled(On("hp"));
        Assert.Equal(new PriorityDecision(PriorityOutcome.FellBack, "hp", "tv"), d);
    }

    [Fact]
    public void EvaluateSettled_LastRankedDisconnects_ReturnsNoneConnected()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("hp"));
        var d = s.EvaluateSettled(On("other"));
        Assert.Equal(PriorityOutcome.NoneConnected, d.Outcome);
        Assert.Null(s.TargetId);
    }

    [Fact]
    public void EvaluateSettled_UnrankedConnects_ReturnsReassertSilently()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("hp"));
        var d = s.EvaluateSettled(On("hp", "usb"));
        Assert.Equal(PriorityOutcome.ReassertSilently, d.Outcome);
        Assert.Equal("hp", s.TargetId);
    }

    [Fact]
    public void EvaluateSettled_FlapEndsWhereItStarted_ReturnsReassertSilently()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("hp"));
        var d = s.EvaluateSettled(On("hp"));
        Assert.Equal(PriorityOutcome.ReassertSilently, d.Outcome);
        Assert.Equal("hp", d.TargetId);
    }

    [Fact]
    public void EvaluateSettled_TwoRankedConnectTogether_SwitchesOnceToHighest()
    {
        var s = Make("tv", "hp", "spk");
        s.Initialize(On("spk"));
        var d = s.EvaluateSettled(On("tv", "hp", "spk"));
        Assert.Equal(new PriorityDecision(PriorityOutcome.SwitchedUp, "tv", "spk"), d);
        Assert.Equal(PriorityOutcome.ReassertSilently, s.EvaluateSettled(On("tv", "hp", "spk")).Outcome);
    }

    [Fact]
    public void EvaluateSettled_OverrideHeld_WhenHigherRankedConnects()
    {
        var s = Make("tv", "hp", "spk");
        s.Initialize(On("spk"));
        s.StartOverride(D("spk"));
        var d = s.EvaluateSettled(On("tv", "hp", "spk"));
        Assert.Equal(PriorityOutcome.ReassertSilently, d.Outcome);
        Assert.Equal("spk", s.OverrideId);
    }

    [Fact]
    public void EvaluateSettled_OverrideOnUnrankedDevice_HeldWhenRankedConnects()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("usb"));
        s.StartOverride(D("usb"));
        var d = s.EvaluateSettled(On("usb", "tv"));
        Assert.Equal(PriorityOutcome.ReassertSilently, d.Outcome);
        Assert.Equal("usb", d.TargetId);
        Assert.Equal("usb", s.OverrideId);
    }

    [Fact]
    public void EvaluateSettled_OverrideDisconnects_EndsOverrideAndFallsBackToTop()
    {
        var s = Make("tv", "hp", "spk");
        s.StartOverride(D("spk"));
        var d = s.EvaluateSettled(On("hp"));
        Assert.Equal(new PriorityDecision(PriorityOutcome.OverrideEnded, "hp", "spk", D("spk")), d);
        Assert.Null(s.OverrideId);
        Assert.Equal("hp", s.TargetId);
    }

    [Fact]
    public void EvaluateSettled_OverrideAbsentDuringStartupGrace_KeepsOverride()
    {
        var s = Make("tv", "hp");
        s.StartOverride(D("usb"));
        s.BeginStartupGrace();
        Assert.True(s.InStartupGrace);

        var d = s.EvaluateSettled(On("hp"));
        Assert.Equal(PriorityOutcome.None, d.Outcome);
        Assert.Equal("usb", s.OverrideId);
        Assert.Null(s.TargetId);

        s.EndStartupGrace();
        Assert.False(s.InStartupGrace);
        var d2 = s.EvaluateSettled(On("hp"));
        Assert.Equal(new PriorityDecision(PriorityOutcome.OverrideEnded, "hp", "usb", D("usb")), d2);
    }

    [Fact]
    public void ReturnToPriority_ClearsOverrideAndReturnsTop()
    {
        var s = Make("tv", "hp", "spk");
        s.StartOverride(D("spk"));
        var top = s.ReturnToPriority(On("hp", "spk"));
        Assert.Equal("hp", top);
        Assert.Null(s.OverrideId);
        Assert.Equal("hp", s.TargetId);
    }

    [Fact]
    public void ReturnToPriority_OverrideIsTopDevice_ReturnsSameTarget()
    {
        var s = Make("tv", "hp");
        s.StartOverride(D("tv"));
        var top = s.ReturnToPriority(On("tv", "hp"));
        Assert.Equal("tv", top);
        Assert.Equal("tv", s.TargetId);
        Assert.Null(s.OverrideId);
    }

    [Fact]
    public void SetRanked_KeepsOverride()
    {
        var s = Make("tv", "hp");
        s.StartOverride(D("hp"));
        s.SetRanked(new[] { new RankedDevice("spk", "spk") });
        Assert.Equal("hp", s.OverrideId);
        Assert.Equal("hp", s.TargetId);
        Assert.Single(s.Ranked);
    }

    [Fact]
    public void EvaluateDefaultChange_IsInternalChange_ReturnsNoAction()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("tv"));
        s.IsInternalChange = true;
        var d = s.EvaluateDefaultChange("hp", _ => true);
        Assert.Equal(RestoreDecision.NoAction, d.Decision);
    }

    [Fact]
    public void EvaluateDefaultChange_NoTarget_ReturnsNoAction()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("other"));
        var d = s.EvaluateDefaultChange("hp", _ => true);
        Assert.Equal(RestoreDecision.NoAction, d.Decision);
    }

    [Fact]
    public void EvaluateDefaultChange_NewDefaultIsTarget_ReturnsNoAction()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("tv"));
        Assert.Equal(RestoreDecision.NoAction, s.EvaluateDefaultChange("tv", _ => true).Decision);
    }

    [Fact]
    public void EvaluateDefaultChange_ExternalChangeToLowerDevice_RestoresTarget()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("tv", "hp"));
        var d = s.EvaluateDefaultChange("hp", _ => true);
        Assert.Equal(new DefaultChangeDecision(RestoreDecision.Restore, "tv"), d);
    }

    [Fact]
    public void EvaluateDefaultChange_TargetInactive_ReturnsSuspend()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("tv", "hp"));
        var d = s.EvaluateDefaultChange("hp", _ => false);
        Assert.Equal(RestoreDecision.Suspend, d.Decision);
    }

    [Fact]
    public void EvaluateDefaultChange_NewDefaultOutranksTarget_NoOverride_ReturnsSuspend()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("hp"));
        var d = s.EvaluateDefaultChange("tv", _ => true);
        Assert.Equal(RestoreDecision.Suspend, d.Decision);
    }

    [Fact]
    public void EvaluateDefaultChange_NewDefaultOutranksOverride_RestoresOverride()
    {
        var s = Make("tv", "hp");
        s.StartOverride(D("hp"));
        var d = s.EvaluateDefaultChange("tv", _ => true);
        Assert.Equal(new DefaultChangeDecision(RestoreDecision.Restore, "hp"), d);
    }

    [Fact]
    public void RankOf_ReturnsOneBasedRankOrNull()
    {
        var s = Make("tv", "hp");
        Assert.Equal(1, s.RankOf("tv"));
        Assert.Equal(2, s.RankOf("hp"));
        Assert.Null(s.RankOf("x"));
    }

    [Fact]
    public void IsRelevant_TrueForRankedAndOverride()
    {
        var s = Make("tv");
        s.StartOverride(D("usb"));
        Assert.True(s.IsRelevant("tv"));
        Assert.True(s.IsRelevant("usb"));
        Assert.False(s.IsRelevant("other"));
    }

    [Fact]
    public void StartOverride_HoldsWholeDevice()
    {
        var s = Make("tv");
        var device = new RankedDevice("usb", "USB name", "USB desc");
        s.StartOverride(device);
        Assert.Equal(device, s.Override);
        Assert.Equal("usb", s.OverrideId);
    }

    [Fact]
    public void EvaluateSettled_OverrideEnded_CarriesEndedOverride()
    {
        var s = Make("tv");
        s.StartOverride(D("usb"));
        var d = s.EvaluateSettled(On("tv"));
        Assert.Equal(PriorityOutcome.OverrideEnded, d.Outcome);
        Assert.Equal(D("usb"), d.EndedOverride);
        Assert.Null(s.Override);
    }

    [Fact]
    public void EvaluateSettled_OtherOutcomes_HaveNoEndedOverride()
    {
        var s = Make("tv", "hp");
        s.Initialize(On("hp"));
        var d = s.EvaluateSettled(On("tv", "hp"));
        Assert.Equal(PriorityOutcome.SwitchedUp, d.Outcome);
        Assert.Null(d.EndedOverride);
    }

    [Theory]
    [InlineData(false, false, "legacy", "ov", "cur", "legacy")]
    [InlineData(false, true, "legacy", "ov", "cur", null)]
    [InlineData(true, true, "legacy", "ov", "cur", "ov")]
    [InlineData(true, false, "legacy", "ov", "cur", "cur")]
    public void PlanPersistedPick_CoversAllFourTransitions(
        bool wasPriority, bool isPriority, string? legacy, string? ov, string? cur, string? expected)
    {
        Assert.Equal(expected, PriorityState.PlanPersistedPick(wasPriority, isPriority, legacy, ov, cur));
    }
}
