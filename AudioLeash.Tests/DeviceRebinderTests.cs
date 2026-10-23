using AudioLeash;

namespace AudioLeash.Tests;

public class DeviceRebinderTests
{
    private const string Tv = "LG TV SSCR2 (NVIDIA High Definition Audio)";
    private const string TvDesc = "NVIDIA High Definition Audio";

    private static RankedDevice Saved(string id, string name = Tv, string? desc = TvDesc) => new(id, name, desc);

    private static DeviceRow Row(string id, bool connected, string name = Tv, string? desc = TvDesc) =>
        new(id, name, desc, connected);

    /// <summary>Rebinds with every candidate row also counted as an existing endpoint, plus <paramref name="alsoExisting"/>.</summary>
    private static IReadOnlyList<RankedDevice> Rebind(
        IReadOnlyList<RankedDevice> saved, IReadOnlyList<DeviceRow> candidates, out bool changed,
        params string[] alsoExisting)
    {
        var existing = candidates.Select(r => r.Id).Concat(alsoExisting).ToHashSet(StringComparer.Ordinal);
        return DeviceRebinder.Rebind(saved, existing, candidates, out changed);
    }

    [Fact]
    public void Rebind_SavedIdGone_SingleConnectedNameMatch_TakesNewId()
    {
        var result = Rebind([Saved("old")], [Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal([Saved("new")], result);
    }

    [Fact]
    public void Rebind_SavedIdStillKnown_IsLeftAlone()
    {
        // An unplugged endpoint still exists, so a same-named connected one is a different device.
        var result = Rebind(
            [Saved("old")], [Row("old", connected: false), Row("new", connected: true)], out bool changed);

        Assert.False(changed);
        Assert.Equal([Saved("old")], result);
    }

    [Fact]
    public void Rebind_PrefersTheSingleConnectedMatchOverDisconnectedOnes()
    {
        var result = Rebind(
            [Saved("old")], [Row("stale", connected: false), Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal("new", result[0].Id);
    }

    [Fact]
    public void Rebind_SeveralConnectedMatches_IsAmbiguousAndLeftAlone()
    {
        var result = Rebind(
            [Saved("old")], [Row("a", connected: true), Row("b", connected: true)], out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_NoConnectedMatch_SingleDisconnectedMatch_TakesIt()
    {
        var result = Rebind([Saved("old")], [Row("new", connected: false)], out bool changed);

        Assert.True(changed);
        Assert.Equal("new", result[0].Id);
    }

    [Fact]
    public void Rebind_NoConnectedMatch_SeveralDisconnectedMatches_IsLeftAlone()
    {
        var result = Rebind(
            [Saved("old")], [Row("a", connected: false), Row("b", connected: false)], out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_DescriptionMustMatch()
    {
        var result = Rebind(
            [Saved("old")], [Row("new", connected: true, desc: "Some Other Driver")], out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_NameMustMatch()
    {
        var result = Rebind(
            [Saved("old")], [Row("new", connected: true, name: "Speakers (Realtek)")], out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_CandidateAlreadyInList_IsNotTaken()
    {
        // "new" is already ranked, so taking it would duplicate it.
        var result = Rebind(
            [Saved("old"), Saved("new")], [Row("new", connected: true)], out bool changed);

        Assert.False(changed);
        Assert.Equal(["old", "new"], result.Select(d => d.Id));
    }

    [Fact]
    public void Rebind_TwoGoneEntriesWithTheSameName_OnlyTheFirstClaimsTheMatch()
    {
        var result = Rebind(
            [Saved("old1"), Saved("old2")], [Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal(["new", "old2"], result.Select(d => d.Id));
    }

    [Fact]
    public void Rebind_KeepsRankOrderAndOtherEntries()
    {
        var phones = Saved("phones", "Headphones (Arctis Nova Elite)", "Arctis Nova Elite");
        var result = Rebind(
            [Saved("old"), phones],
            [Row("phones", connected: true, phones.Name, phones.Description), Row("new", connected: true)],
            out bool changed);

        Assert.True(changed);
        Assert.Equal([Saved("new"), phones], result);
    }

    [Fact]
    public void Rebind_TakesTheCurrentNameAndDescriptionOfTheMatch()
    {
        // Name comparison ignores case; the rebound entry caches what Windows reports now.
        var result = Rebind(
            [Saved("old", Tv.ToUpperInvariant())], [Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal(Saved("new"), result[0]);
    }

    [Fact]
    public void Rebind_NullDescriptionsMatchEachOther()
    {
        var result = Rebind(
            [Saved("old", desc: null)], [Row("new", connected: true, desc: null)], out bool changed);

        Assert.True(changed);
        Assert.Equal("new", result[0].Id);
    }

    [Fact]
    public void Rebind_EmptyList_IsUnchanged()
    {
        var result = Rebind([], [Row("new", connected: true)], out bool changed);

        Assert.False(changed);
        Assert.Empty(result);
    }

    [Fact]
    public void Rebind_SavedIdStillExistsAsNotPresent_IsLeftAlone()
    {
        // A USB headset that is unplugged goes NotPresent: it still exists, so an identical
        // model that is connected must not take over its entry.
        var result = Rebind([Saved("old")], [Row("new", connected: true)], out bool changed, alsoExisting: "old");

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_DescriptionComparisonIgnoresCase()
    {
        var result = Rebind(
            [Saved("old", desc: TvDesc.ToLowerInvariant())], [Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal(Saved("new"), result[0]);
    }

    [Fact]
    public void Rebind_NoKnownDevices_IsUnchanged()
    {
        var result = Rebind([Saved("old")], [], out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_DisconnectedMatchClaimedEarlierInThePass_IsNotTakenTwice()
    {
        var result = Rebind([Saved("old1"), Saved("old2")], [Row("new", connected: false)], out bool changed);

        Assert.True(changed);
        Assert.Equal(["new", "old2"], result.Select(d => d.Id));
    }

    [Fact]
    public void Rebind_SavedDescriptionNotRecorded_MatchesOnNameAlone()
    {
        // A saved pick stores only the friendly name, which already embeds the description.
        var result = Rebind([Saved("old", desc: null)], [Row("new", connected: true)], out bool changed);

        Assert.True(changed);
        Assert.Equal(Saved("new"), result[0]);
    }

    [Fact]
    public void Rebind_SavedDescriptionNotRecorded_SeveralNameMatches_IsLeftAlone()
    {
        var result = Rebind(
            [Saved("old", desc: null)],
            [Row("a", connected: true), Row("b", connected: true, desc: "Other Driver")],
            out bool changed);

        Assert.False(changed);
        Assert.Equal("old", result[0].Id);
    }

    [Fact]
    public void Rebind_SavedDescriptionNotRecorded_PrefersTheSingleConnectedMatch()
    {
        var result = Rebind(
            [Saved("old", desc: null)], [Row("stale", connected: false), Row("new", connected: true)],
            out bool changed);

        Assert.True(changed);
        Assert.Equal("new", result[0].Id);
    }

    [Fact]
    public void Rebind_SavedDescriptionNotRecorded_SingleDisconnectedMatch_TakesIt()
    {
        var result = Rebind([Saved("old", desc: null)], [Row("new", connected: false)], out bool changed);

        Assert.True(changed);
        Assert.Equal("new", result[0].Id);
    }
}
