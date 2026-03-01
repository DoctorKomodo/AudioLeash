#nullable enable
using System;
using System.IO;
using AudioLeash;

namespace AudioLeash.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"AudioLeash-Test-{Guid.NewGuid()}");

    private SettingsService Svc() => new(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileAbsent_ReturnsNull()
    {
        Assert.Null(Svc().LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_AfterSaveWithId_ReturnsSameId()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceId("device-123");
        Assert.Equal("device-123", svc.LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_AfterSaveNull_ReturnsNull()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceId("device-123");
        svc.SaveSelectedDeviceId(null);
        Assert.Null(svc.LoadSelectedDeviceId());
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsNull()
    {
        var svc = Svc();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not valid json{{{{");
        Assert.Null(svc.LoadSelectedDeviceId());
    }

    [Fact]
    public void HasSettingsFile_WhenFileAbsent_ReturnsFalse()
    {
        Assert.False(Svc().HasSettingsFile);
    }

    [Fact]
    public void HasSettingsFile_AfterSaveWithId_ReturnsTrue()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceId("device-123");
        Assert.True(svc.HasSettingsFile);
    }

    [Fact]
    public void HasSettingsFile_AfterSaveNull_ReturnsTrueDistinguishingFromAbsent()
    {
        // Saving null writes the file (user explicitly cleared selection).
        // HasSettingsFile must return true so the caller can distinguish this
        // from a genuine first run where no file exists.
        var svc = Svc();
        svc.SaveSelectedDeviceId(null);
        Assert.True(svc.HasSettingsFile);
        Assert.Null(svc.LoadSelectedDeviceId());
    }

    // ── Playback + Capture persistence ──────────────────────────────────

    [Fact]
    public void LoadPlaybackId_WhenFileAbsent_ReturnsNull()
    {
        Assert.Null(Svc().LoadSelectedPlaybackDeviceId());
    }

    [Fact]
    public void LoadCaptureId_WhenFileAbsent_ReturnsNull()
    {
        Assert.Null(Svc().LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void SaveAndLoadPlaybackId_RoundTrips()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: null);
        Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
        Assert.Null(svc.LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void SaveAndLoadCaptureId_RoundTrips()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: null, captureId: "cap-456");
        Assert.Null(svc.LoadSelectedPlaybackDeviceId());
        Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void SaveAndLoadBothIds_RoundTrips()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
        Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
        Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void SavePlaybackId_PreservesCaptureId()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
        svc.SaveSelectedPlaybackDeviceId("pb-new");
        Assert.Equal("pb-new", svc.LoadSelectedPlaybackDeviceId());
        Assert.Equal("cap-456", svc.LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void SaveCaptureId_PreservesPlaybackId()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
        svc.SaveSelectedCaptureDeviceId("cap-new");
        Assert.Equal("pb-123", svc.LoadSelectedPlaybackDeviceId());
        Assert.Equal("cap-new", svc.LoadSelectedCaptureDeviceId());
    }

    // ── Backward compatibility (migration from old single-field format) ──

    [Fact]
    public void LoadPlaybackId_MigratesOldSelectedDeviceIdField()
    {
        var svc = Svc();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "settings.json"),
            """{"selectedDeviceId": "old-device"}""");

        Assert.Equal("old-device", svc.LoadSelectedPlaybackDeviceId());
        Assert.Null(svc.LoadSelectedCaptureDeviceId());
    }

    [Fact]
    public void LoadPlaybackId_NewFieldTakesPrecedenceOverOldField()
    {
        var svc = Svc();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "settings.json"),
            """{"selectedDeviceId": "old-device", "selectedPlaybackDeviceId": "new-device"}""");

        Assert.Equal("new-device", svc.LoadSelectedPlaybackDeviceId());
    }

    [Fact]
    public void SaveCaptureId_MigratesLegacyFieldToPlayback()
    {
        // When a legacy-format file exists and SaveSelectedCaptureDeviceId is called,
        // the old selectedDeviceId value should be migrated to selectedPlaybackDeviceId.
        var svc = Svc();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "settings.json"),
            """{"selectedDeviceId": "legacy-pb"}""");

        svc.SaveSelectedCaptureDeviceId("cap-new");
        Assert.Equal("legacy-pb", svc.LoadSelectedPlaybackDeviceId());
        Assert.Equal("cap-new", svc.LoadSelectedCaptureDeviceId());
    }

    // ── Clear both ──────────────────────────────────────────────────────

    [Fact]
    public void ClearAll_ClearsBothIds()
    {
        var svc = Svc();
        svc.SaveSelectedDeviceIds(playbackId: "pb-123", captureId: "cap-456");
        svc.SaveSelectedDeviceIds(playbackId: null, captureId: null);
        Assert.Null(svc.LoadSelectedPlaybackDeviceId());
        Assert.Null(svc.LoadSelectedCaptureDeviceId());
        Assert.True(svc.HasSettingsFile);
    }
}
