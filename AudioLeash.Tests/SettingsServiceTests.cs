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
}
