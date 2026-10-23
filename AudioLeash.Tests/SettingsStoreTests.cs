#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using AudioLeash;
using Xunit;

namespace AudioLeash.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AudioLeashTests_" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    private string BakPath => Path.Combine(_dir, "settings.json.bak");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SettingsSnapshot NonEmpty() => SettingsSnapshot.Empty with
    {
        KeepAlive = new[] { new RankedDevice("ka", "Keep") },
        Playback = new FlowSnapshot(new RankedDevice("p", "Speakers"), new[] { new RankedDevice("p", "Speakers") }),
    };

    private void WriteRaw(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, text);
    }

    /// <summary>Real file system with call counting and injectable faults.</summary>
    private sealed class FakeFs : ISettingsFileSystem
    {
        public int WriteCount;
        public int ReadCount;
        public List<TimeSpan> Delays { get; } = new();
        public int ReadFailuresRemaining;
        public int CopyFailuresRemaining;
        public bool FailWrites;

        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path)
        {
            ReadCount++;
            if (ReadFailuresRemaining > 0)
            {
                ReadFailuresRemaining--;
                throw new IOException("locked");
            }
            return File.ReadAllText(path);
        }

        public void WriteAllText(string path, string text)
        {
            WriteCount++;
            if (FailWrites) throw new IOException("write failed");
            File.WriteAllText(path, text);
        }

        public void Copy(string source, string dest, bool overwrite)
        {
            if (CopyFailuresRemaining > 0)
            {
                CopyFailuresRemaining--;
                throw new IOException("copy failed");
            }
            File.Copy(source, dest, overwrite);
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void Delay(TimeSpan delay) => Delays.Add(delay);
    }

    [Fact]
    public void Load_NoFile_ReturnsEmptyAndHasSettingsFileFalse()
    {
        var store = new SettingsStore(_dir);
        Assert.Equal(SettingsSnapshot.Empty, store.Load());
        Assert.False(store.HasSettingsFile);
    }

    [Fact]
    public void Save_EmptySnapshotWithNoFile_WritesNothing()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        store.Save(SettingsSnapshot.Empty);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Save_ThenNewStoreLoad_RoundTrips()
    {
        var first = new SettingsStore(_dir);
        first.Load();
        first.Save(NonEmpty());

        var second = new SettingsStore(_dir);
        var loaded = second.Load();
        Assert.True(second.HasSettingsFile);
        Assert.Equal("p", loaded.Playback.Pick?.Id);
        Assert.Equal("ka", Assert.Single(loaded.KeepAlive).Id);
    }

    [Fact]
    public void Save_IdenticalSnapshot_DoesNotWriteAgain()
    {
        var fs = new FakeFs();
        var store = new SettingsStore(_dir, fs);
        store.Load();
        store.Save(NonEmpty());
        store.Save(NonEmpty());
        Assert.Equal(1, fs.WriteCount);
    }

    [Fact]
    public void Load_V1File_UpgradedOnFirstSave()
    {
        WriteRaw("{\"selectedPlaybackDeviceId\":\"old\",\"selectedPlaybackDeviceName\":\"Old\"}");
        var store = new SettingsStore(_dir);
        var loaded = store.Load();
        Assert.Equal("old", loaded.Playback.Pick?.Id);

        store.Save(loaded);
        Assert.Contains("\"version\": 2", File.ReadAllText(FilePath));
        Assert.False(File.Exists(BakPath));
    }

    [Fact]
    public void Load_LockedFile_RetriesThenSucceeds()
    {
        var first = new SettingsStore(_dir);
        first.Load();
        first.Save(NonEmpty());

        var fs = new FakeFs { ReadFailuresRemaining = 2 };
        var store = new SettingsStore(_dir, fs);
        var loaded = store.Load();

        Assert.Equal("p", loaded.Playback.Pick?.Id);
        Assert.Equal(3, fs.ReadCount);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250) }, fs.Delays);
    }

    [Fact]
    public void Load_LockedFileForAllAttempts_ReturnsEmpty()
    {
        WriteRaw("{}");
        var fs = new FakeFs { ReadFailuresRemaining = int.MaxValue };
        var store = new SettingsStore(_dir, fs);

        Assert.Equal(SettingsSnapshot.Empty, store.Load());
        Assert.Equal(4, fs.ReadCount);
        Assert.Equal(3, fs.Delays.Count);
        Assert.True(store.HasSettingsFile);
    }

    [Fact]
    public void Save_AfterCorruptLoad_BacksUpThenWrites()
    {
        WriteRaw("not json {");
        var store = new SettingsStore(_dir);
        Assert.Equal(SettingsSnapshot.Empty, store.Load());

        store.Save(NonEmpty());

        Assert.Equal("not json {", File.ReadAllText(BakPath));
        Assert.Equal(SettingsFile.Serialize(NonEmpty()), File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_AfterUnusableLoad_BackupFails_WritesNothingAndRetriesNextSave()
    {
        WriteRaw("not json {");
        var fs = new FakeFs { CopyFailuresRemaining = 1 };
        var store = new SettingsStore(_dir, fs);
        store.Load();

        store.Save(NonEmpty());
        Assert.Equal(0, fs.WriteCount);
        Assert.Equal("not json {", File.ReadAllText(FilePath));

        store.Save(NonEmpty());
        Assert.Equal(1, fs.WriteCount);
        Assert.Equal("not json {", File.ReadAllText(BakPath));
    }

    [Fact]
    public void Save_GoodFile_NeverBacksUp()
    {
        var first = new SettingsStore(_dir);
        first.Load();
        first.Save(NonEmpty());

        var store = new SettingsStore(_dir);
        store.Load();
        store.Save(SettingsSnapshot.Empty);

        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(BakPath));
    }

    [Fact]
    public void Save_NoFile_NeverBacksUp()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        store.Save(NonEmpty());
        Assert.False(File.Exists(BakPath));
    }

    [Fact]
    public void Save_WriteFails_DoesNotThrow()
    {
        var fs = new FakeFs { FailWrites = true };
        var store = new SettingsStore(_dir, fs);
        store.Load();
        var ex = Record.Exception(() => store.Save(NonEmpty()));
        Assert.Null(ex);
        Assert.Equal(1, fs.WriteCount);
    }
}
