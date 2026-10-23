#nullable enable
using System;
using System.Collections.Generic;
using Xunit;

namespace AudioLeash.Tests;

public class SettingsFileTests
{
    private static readonly RankedDevice A = new("a", "Device A", "Desc A");
    private static readonly RankedDevice B = new("b", "Device B", "Desc B");
    private static readonly RankedDevice C = new("c", "Device C", "Desc C");

    // ── FlowSnapshot.From ───────────────────────────────────────────────

    [Fact]
    public void FlowSnapshot_From_PriorityMode_TakesOverride()
    {
        var flow = FlowSnapshot.From(new[] { A }, B, C);
        Assert.Equal(B, flow.Pick);
        Assert.Equal(new[] { A }, flow.Priority);
    }

    [Fact]
    public void FlowSnapshot_From_LegacyMode_TakesSelected()
    {
        var flow = FlowSnapshot.From(Array.Empty<RankedDevice>(), B, C);
        Assert.Equal(C, flow.Pick);
    }

    [Fact]
    public void FlowSnapshot_From_NoPick_IsNull()
    {
        var flow = FlowSnapshot.From(Array.Empty<RankedDevice>(), null, null);
        Assert.Null(flow.Pick);
        Assert.Empty(flow.Priority);
    }

    // ── Serialize / Parse ───────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenParse_RoundTrips()
    {
        var snapshot = new SettingsSnapshot(
            new FlowSnapshot(A, new[] { A, B }),
            new FlowSnapshot(null, new[] { C }),
            new[] { B });

        var parsed = SettingsFile.Parse(SettingsFile.Serialize(snapshot));

        Assert.NotNull(parsed);
        Assert.Equal(A, parsed!.Playback.Pick);
        Assert.Equal(new[] { A, B }, parsed.Playback.Priority);
        Assert.Null(parsed.Recording.Pick);
        Assert.Equal(new[] { C }, parsed.Recording.Priority);
        Assert.Equal(new[] { B }, parsed.KeepAlive);
        Assert.Equal("Desc A", parsed.Playback.Pick!.Description);
    }

    [Fact]
    public void Serialize_WritesVersion2AndCamelCase()
    {
        var json = SettingsFile.Serialize(new SettingsSnapshot(
            new FlowSnapshot(A, new[] { A }), FlowSnapshot.Empty, Array.Empty<RankedDevice>()));

        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"playback\"", json);
        Assert.Contains("\"recording\"", json);
        Assert.Contains("\"keepAlive\"", json);
        Assert.Contains("\"pick\"", json);
        Assert.Contains("\"priority\"", json);
    }

    [Fact]
    public void Parse_UserV21File_Upgrades()
    {
        const string json = """
            {"selectedDeviceId":null,"selectedPlaybackDeviceId":null,"selectedCaptureDeviceId":null,"selectedPlaybackDeviceName":null,"selectedCaptureDeviceName":null,
             "playbackPriority":[{"id":"{0.0.0.00000000}.{c0df5970-fe89-4405-b2d1-3e76cd2e0868}","name":"LG TV SSCR2 (NVIDIA High Definition Audio)","description":"NVIDIA High Definition Audio"},{"id":"{0.0.0.00000000}.{5472e742-afe0-4efe-8445-eff9aba2bea5}","name":"Headphones (Arctis Nova Elite)","description":"Arctis Nova Elite"}],
             "capturePriority":[{"id":"{0.0.1.00000000}.{c51338b5-079c-41b7-9f62-c603f0855872}","name":"Microphone (Arctis Nova Elite)","description":"Arctis Nova Elite"}],
             "keepAliveDevices":[{"id":"{0.0.0.00000000}.{c0df5970-fe89-4405-b2d1-3e76cd2e0868}","name":"LG TV SSCR2 (NVIDIA High Definition Audio)","description":"NVIDIA High Definition Audio"}]}
            """;

        var s = SettingsFile.Parse(json);

        Assert.NotNull(s);
        Assert.Null(s!.Playback.Pick);
        Assert.Null(s.Recording.Pick);
        Assert.Equal(2, s.Playback.Priority.Count);
        Assert.Equal(new RankedDevice("{0.0.0.00000000}.{c0df5970-fe89-4405-b2d1-3e76cd2e0868}",
            "LG TV SSCR2 (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio"), s.Playback.Priority[0]);
        Assert.Equal(new RankedDevice("{0.0.0.00000000}.{5472e742-afe0-4efe-8445-eff9aba2bea5}",
            "Headphones (Arctis Nova Elite)", "Arctis Nova Elite"), s.Playback.Priority[1]);
        var mic = Assert.Single(s.Recording.Priority);
        Assert.Equal("Microphone (Arctis Nova Elite)", mic.Name);
        Assert.Equal("Arctis Nova Elite", mic.Description);
        var tv = Assert.Single(s.KeepAlive);
        Assert.Equal("LG TV SSCR2 (NVIDIA High Definition Audio)", tv.Name);
    }

    [Fact]
    public void Parse_V1Picks_MapWithNullDescription()
    {
        const string json = """
            {"selectedPlaybackDeviceId":"p","selectedPlaybackDeviceName":"Speakers",
             "selectedCaptureDeviceId":"c","selectedCaptureDeviceName":"Mic"}
            """;

        var s = SettingsFile.Parse(json);

        Assert.Equal(new RankedDevice("p", "Speakers"), s!.Playback.Pick);
        Assert.Null(s.Playback.Pick!.Description);
        Assert.Equal(new RankedDevice("c", "Mic"), s.Recording.Pick);
        Assert.Null(s.Recording.Pick!.Description);
    }

    [Fact]
    public void Parse_V1_LegacySelectedDeviceId_IsPlaybackFallback()
    {
        const string json = """{"selectedDeviceId":"legacy","selectedPlaybackDeviceName":"Old Speakers"}""";

        var s = SettingsFile.Parse(json);

        Assert.Equal(new RankedDevice("legacy", "Old Speakers"), s!.Playback.Pick);
        Assert.Null(s.Recording.Pick);
    }

    [Fact]
    public void Parse_V1_PickWithoutName_GetsUnknownDevice()
    {
        var s = SettingsFile.Parse("""{"selectedPlaybackDeviceId":"p"}""");

        Assert.Equal("Unknown device", s!.Playback.Pick!.Name);
    }

    [Fact]
    public void Parse_SanitizesListsAndPick()
    {
        const string json = """
            {"version":2,
             "playback":{"pick":{"id":"","name":"x"},
                         "priority":[null,{"id":"","name":"empty"},{"name":"missing id"},
                                     {"id":"a","name":"First"},{"id":"a","name":"Second"},
                                     {"id":"b","name":null}]},
             "recording":{"pick":{"id":"r"},"priority":null},
             "keepAlive":[{"id":"k","name":"K"},{"id":"k","name":"Dup"}]}
            """;

        var s = SettingsFile.Parse(json);

        Assert.NotNull(s);
        Assert.Null(s!.Playback.Pick);
        Assert.Equal(2, s.Playback.Priority.Count);
        Assert.Equal("First", s.Playback.Priority[0].Name);
        Assert.Equal(new RankedDevice("b", "Unknown device"), s.Playback.Priority[1]);
        Assert.Equal(new RankedDevice("r", "Unknown device"), s.Recording.Pick);
        Assert.Empty(s.Recording.Priority);
        Assert.Equal("K", Assert.Single(s.KeepAlive).Name);
    }

    [Fact]
    public void Parse_FutureVersion_ReturnsNull()
        => Assert.Null(SettingsFile.Parse("""{"version":3}"""));

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
        => Assert.Null(SettingsFile.Parse("not json"));

    [Fact]
    public void Parse_EmptyObject_IsEmptySnapshot()
    {
        var s = SettingsFile.Parse("{}");

        Assert.NotNull(s);
        Assert.Null(s!.Playback.Pick);
        Assert.Null(s.Recording.Pick);
        Assert.Empty(s.Playback.Priority);
        Assert.Empty(s.Recording.Priority);
        Assert.Empty(s.KeepAlive);
    }
}
