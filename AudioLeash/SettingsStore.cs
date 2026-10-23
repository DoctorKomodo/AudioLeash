#nullable enable
using System;
using System.IO;
using System.Threading;

namespace AudioLeash;

/// <summary>File operations the settings store needs, so tests can inject faults.</summary>
internal interface ISettingsFileSystem
{
    bool Exists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string text);
    void Copy(string source, string dest, bool overwrite);
    void CreateDirectory(string path);
    void Delay(TimeSpan delay);
}

/// <summary>
/// Reads and writes %AppData%\AudioLeash\settings.json as a <see cref="SettingsSnapshot"/>.
/// Neither <see cref="Load"/> nor <see cref="Save"/> throws. All calls are made on the UI
/// thread, so no locking is needed.
/// </summary>
public sealed class SettingsStore
{
    private const int ReadAttempts = 4;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _directory;
    private readonly ISettingsFileSystem _fs;

    // The JSON last loaded or written; Save skips the write when the new JSON matches it.
    private string? _lastJson;
    // Set when the file existed but could not be used; the file is copied to .bak before
    // the first overwrite.
    private bool _backupPending;

    private static readonly string EmptyJson = SettingsFile.Serialize(SettingsSnapshot.Empty);

    private string FilePath => Path.Combine(_directory, "settings.json");
    private string BackupPath => FilePath + ".bak";

    /// <summary>Production constructor — uses %AppData%\AudioLeash\.</summary>
    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioLeash"))
    { }

    /// <summary>Test constructor — uses the provided directory and optional file system.</summary>
    internal SettingsStore(string directory, ISettingsFileSystem? fileSystem = null)
    {
        _directory = directory;
        _fs = fileSystem ?? new RealFileSystem();
    }

    /// <summary>
    /// True if the settings file existed at <see cref="Load"/>, regardless of its contents.
    /// Distinguishes a genuine first run (no file) from an explicit empty state.
    /// </summary>
    public bool HasSettingsFile { get; private set; }

    /// <summary>
    /// Loads the settings. A missing or unusable file gives <see cref="SettingsSnapshot.Empty"/>;
    /// an unusable file is backed up before the first write.
    /// </summary>
    public SettingsSnapshot Load()
    {
        try
        {
            HasSettingsFile = _fs.Exists(FilePath);
            if (!HasSettingsFile)
            {
                _lastJson = EmptyJson;
                _backupPending = false;
                return SettingsSnapshot.Empty;
            }

            var json = ReadWithRetry();
            var snapshot = json is null ? null : SettingsFile.Parse(json);
            if (snapshot is not null)
            {
                _lastJson = json;
                _backupPending = false;
                return snapshot;
            }
        }
        catch (Exception)
        {
            // fall through to unusable
        }

        _lastJson = null;
        _backupPending = true;
        return SettingsSnapshot.Empty;
    }

    /// <summary>
    /// Writes the snapshot unless it serializes to what was last loaded or written. Best-effort:
    /// failures are swallowed. If the previous file was unusable it is copied to .bak first;
    /// when that copy fails nothing is written and the copy is retried on the next save.
    /// An empty snapshot never replaces an unusable file: it is left alone until there is something to save.
    /// </summary>
    public void Save(SettingsSnapshot snapshot)
    {
        try
        {
            var json = SettingsFile.Serialize(snapshot);
            if (json == _lastJson) return;
            if (_backupPending && json == EmptyJson) return;

            // Nothing left to protect if the unusable file has since disappeared.
            if (_backupPending && !_fs.Exists(FilePath)) _backupPending = false;

            if (_backupPending)
            {
                _fs.Copy(FilePath, BackupPath, overwrite: true);
                _backupPending = false;
            }

            _fs.CreateDirectory(_directory);
            _fs.WriteAllText(FilePath, json);
            _lastJson = json;
        }
        catch (Exception) { /* best-effort */ }
    }

    // Retries only on IOException (e.g. another process holding the file); null when all attempts fail.
    private string? ReadWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return _fs.ReadAllText(FilePath);
            }
            catch (IOException) when (attempt < ReadAttempts)
            {
                _fs.Delay(ReadRetryDelay);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private sealed class RealFileSystem : ISettingsFileSystem
    {
        public bool Exists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
        public void Copy(string source, string dest, bool overwrite) => File.Copy(source, dest, overwrite);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void Delay(TimeSpan delay) => Thread.Sleep(delay);
    }
}
