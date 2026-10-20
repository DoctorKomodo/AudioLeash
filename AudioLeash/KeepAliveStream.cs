#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioLeash;

/// <summary>
/// A shared-mode render stream of silence on one playback device, keeping its digital link
/// (HDMI, S/PDIF) awake. Other apps' audio mixes in normally.
/// </summary>
internal sealed class KeepAliveStream : IKeepAliveStream
{
    /// <summary>
    /// Buffer length. Polling mode wakes every <c>LatencyMs / 2</c>, so about five times a second;
    /// NAudio's event-sync mode would wake on every ~10 ms engine period instead.
    /// </summary>
    private const int LatencyMs = 400;

    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly WasapiOut _output;
    private readonly Action<KeepAliveStream> _onFaulted;
    private volatile bool _disposing;

    private KeepAliveStream(
        string deviceId, MMDeviceEnumerator enumerator, MMDevice device, WasapiOut output, Action<KeepAliveStream> onFaulted)
    {
        DeviceId    = deviceId;
        _enumerator = enumerator;
        _device     = device;
        _output     = output;
        _onFaulted  = onFaulted;
        _output.PlaybackStopped += OnPlaybackStopped;
    }

    public string DeviceId { get; }

    /// <summary>
    /// Starts streaming silence to the device; returns null (and logs) on any failure.
    /// <paramref name="onFaulted"/> is called on the playback thread if the stream later stops
    /// without <see cref="Dispose"/> being called (device removed, taken in exclusive mode, audio
    /// service restarted). It is wired up before playback starts, so even an immediate failure is
    /// reported. It must not call <see cref="Dispose"/> synchronously: that joins the playback thread.
    /// </summary>
    public static KeepAliveStream? TryStart(string deviceId, Action<KeepAliveStream> onFaulted)
    {
        try
        {
            // All COM work runs on a thread-pool (MTA) thread: no STA object from the UI thread
            // crosses apartments, and WasapiOut captures no WinForms SynchronizationContext, so
            // PlaybackStopped is raised directly on its own playback thread.
            return Task.Run(() => Create(deviceId, onFaulted)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Keep-alive could not start on {deviceId}: {ex.Message}");
            return null;
        }
    }

    private static KeepAliveStream Create(string deviceId, Action<KeepAliveStream> onFaulted)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        WasapiOut? output = null;
        try
        {
            device = enumerator.GetDevice(deviceId);
            output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, LatencyMs);
            // Before Init, OutputWaveFormat is the device's mix format — no conversion needed.
            output.Init(new SilenceWaveProvider(output.OutputWaveFormat));
            var stream = new KeepAliveStream(deviceId, enumerator, device, output, onFaulted);
            output.Play();
            return stream;
        }
        catch
        {
            output?.Dispose();
            device?.Dispose();
            enumerator.Dispose();
            throw;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposing) return;
        if (e.Exception is not null)
            Debug.WriteLine($"Keep-alive stream on {DeviceId} stopped: {e.Exception.Message}");
        _onFaulted(this);
    }

    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        try { _output.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"Keep-alive stream dispose failed: {ex.Message}"); }
        _device.Dispose();
        _enumerator.Dispose();
    }
}
