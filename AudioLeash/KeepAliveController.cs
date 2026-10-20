#nullable enable
using System;

namespace AudioLeash;

/// <summary>A running keep-alive stream, as <see cref="KeepAliveController"/> sees it.</summary>
internal interface IKeepAliveStream : IDisposable
{
    string DeviceId { get; }
}

/// <summary>
/// Keeps at most one keep-alive stream running to match <see cref="KeepAliveState.DesiredId"/>.
/// All calls come from the UI thread, but starting or stopping a stream blocks it in a CLR wait
/// that still delivers sent messages (power and session notifications), so
/// <see cref="Reconcile"/> can be re-entered; a re-entrant call is folded into another pass of
/// the outer one, so no stream is ever orphaned. Audio objects are injected, so this is unit-testable.
/// </summary>
internal sealed class KeepAliveController(
    KeepAliveState state,
    Func<string?> getDefaultId,
    Func<string, Action<IKeepAliveStream>, IKeepAliveStream?> startStream,
    Action<bool> setRetryArmed) : IDisposable
{
    private IKeepAliveStream? _current;
    private IKeepAliveStream? _faultedWhileStarting;
    private bool _reconciling;
    private bool _rerun;
    private bool _disposed;

    public KeepAliveState State => state;

    /// <summary>The device currently streaming, or null.</summary>
    public string? RunningId => _current?.DeviceId;

    /// <summary>Re-reads the Windows default playback device, then reconciles.</summary>
    public void RefreshDefault()
    {
        state.DefaultId = getDefaultId();
        Reconcile();
    }

    /// <summary>
    /// Enables or disables keep-alive for a device and applies it. Re-reads the default first:
    /// it isn't tracked while nothing is enabled, so it may be stale. Returns whether anything changed.
    /// </summary>
    public bool SetEnabled(RankedDevice device, bool enabled)
    {
        if (!state.SetEnabled(device, enabled)) return false;
        RefreshDefault();
        return true;
    }

    /// <summary>Stops and/or starts the stream to match the state; arms the retry when a wanted start fails.</summary>
    public void Reconcile()
    {
        if (_disposed) return;
        if (_reconciling)
        {
            _rerun = true;
            return;
        }

        _reconciling = true;
        try
        {
            do
            {
                _rerun = false;
                ReconcileOnce();
            }
            while (_rerun && !_disposed);
        }
        finally { _reconciling = false; }
    }

    private void ReconcileOnce()
    {
        var plan = state.Plan(_current?.DeviceId);
        if (plan.StopCurrent && _current is { } old)
        {
            _current = null; // detach before Dispose, which can re-enter
            old.Dispose();
        }

        bool retry = false;
        if (plan.StartId is { } id)
        {
            _faultedWhileStarting = null;
            var stream = startStream(id, OnFaulted);
            if (stream is null)
                retry = true;
            else if (ReferenceEquals(_faultedWhileStarting, stream) || _disposed)
            {
                stream.Dispose();
                retry = !_disposed;
            }
            else
                _current = stream;
            _faultedWhileStarting = null;
        }
        setRetryArmed(retry);
    }

    /// <summary>
    /// A stream stopped unexpectedly (call on the UI thread). The current stream is dropped and
    /// retried later rather than at once, so a device that fails straight away can't spin.
    /// </summary>
    public void OnFaulted(IKeepAliveStream stream)
    {
        if (_disposed) return;
        if (ReferenceEquals(stream, _current))
        {
            _current = null;
            stream.Dispose();
            setRetryArmed(true);
        }
        else
        {
            // Still being started (or stale, which is harmless): the start path checks this.
            _faultedWhileStarting = stream;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var current = _current;
        _current = null;
        current?.Dispose();
    }
}
