#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioLeash;

/// <summary>What a settled reconcile of the ranked list decided.</summary>
internal enum PriorityOutcome
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>The enforced device is unchanged and connected — re-assert it without notifying.</summary>
    ReassertSilently,

    /// <summary>A higher-ranked device connected — switch to it and notify.</summary>
    SwitchedUp,

    /// <summary>The enforced device went away — fall back to the next one and notify.</summary>
    FellBack,

    /// <summary>The manual override device disconnected — priority order resumes.</summary>
    OverrideEnded,

    /// <summary>No ranked device is connected — leave Windows' default alone.</summary>
    NoneConnected,
}

internal readonly record struct PriorityDecision(PriorityOutcome Outcome, string? TargetId, string? PreviousTargetId);

/// <summary>
/// Pure state machine for ranked device priority. Contains no WinForms or audio-stack
/// dependencies — fully unit-testable. All mutable state except
/// <see cref="IsInternalChange"/> is guarded by one private lock, because
/// <see cref="IsRelevant"/> and <see cref="EvaluateDefaultChange"/> run on the audio thread.
/// </summary>
internal sealed class PriorityState
{
    private readonly object _lock = new();
    private IReadOnlyList<RankedDevice> _ranked = Array.Empty<RankedDevice>();
    private string? _overrideId;
    private string? _targetId;
    private bool _inStartupGrace;

    private volatile bool _isInternalChange;

    /// <summary>
    /// Set to <c>true</c> while the app itself is switching the default device,
    /// to prevent the change-notification handler from triggering a feedback loop.
    /// Same semantics as <see cref="DeviceSelectionState.IsInternalChange"/>.
    /// </summary>
    public bool IsInternalChange
    {
        get => _isInternalChange;
        set => _isInternalChange = value;
    }

    /// <summary>The current ranked list snapshot, highest priority first.</summary>
    public IReadOnlyList<RankedDevice> Ranked { get { lock (_lock) return _ranked; } }

    /// <summary>True when a ranked list is configured (otherwise legacy behaviour applies).</summary>
    public bool IsPriorityMode { get { lock (_lock) return _ranked.Count > 0; } }

    /// <summary>The manually chosen device that currently overrides the order, if any.</summary>
    public string? OverrideId { get { lock (_lock) return _overrideId; } }

    /// <summary>The last committed enforced device, or null when none is enforced.</summary>
    public string? TargetId { get { lock (_lock) return _targetId; } }

    /// <summary>True while a persisted override may simply not have enumerated yet (app startup).</summary>
    public bool InStartupGrace { get { lock (_lock) return _inStartupGrace; } }

    /// <summary>Replaces the ranked list. Does not touch the override or the target.</summary>
    public void SetRanked(IReadOnlyList<RankedDevice> ranked)
    {
        var copy = ranked.ToList();
        lock (_lock) _ranked = copy;
    }

    /// <summary>The 1-based rank of <paramref name="id"/>, or null when unranked.</summary>
    public int? RankOf(string id)
    {
        lock (_lock) return RankOfLocked(id);
    }

    /// <summary>True when the device is ranked or is the current override.</summary>
    public bool IsRelevant(string id)
    {
        lock (_lock) return id == _overrideId || RankOfLocked(id) is not null;
    }

    /// <summary>Commits the startup target without producing a decision.</summary>
    public void Initialize(IReadOnlySet<string> connected)
    {
        lock (_lock)
        {
            _targetId = _overrideId is not null
                ? (connected.Contains(_overrideId) ? _overrideId : null)
                : TopLocked(connected);
        }
    }

    /// <summary>Sets the manual override (and the enforced target) to <paramref name="id"/>.</summary>
    public void StartOverride(string id)
    {
        lock (_lock)
        {
            _overrideId = id;
            _targetId = id;
        }
    }

    /// <summary>Clears the override, commits and returns the top connected ranked device (or null).</summary>
    public string? ReturnToPriority(IReadOnlySet<string> connected)
    {
        lock (_lock)
        {
            _overrideId = null;
            _targetId = TopLocked(connected);
            return _targetId;
        }
    }

    public void BeginStartupGrace() { lock (_lock) _inStartupGrace = true; }

    public void EndStartupGrace() { lock (_lock) _inStartupGrace = false; }

    /// <summary>
    /// Reconciles the enforced device with the settled set of connected devices.
    /// Commits the new target; the outcome reflects the net change since the last call.
    /// </summary>
    public PriorityDecision EvaluateSettled(IReadOnlySet<string> connected)
    {
        lock (_lock)
        {
            if (_overrideId is not null)
            {
                if (connected.Contains(_overrideId))
                {
                    _targetId = _overrideId;
                    return new PriorityDecision(PriorityOutcome.ReassertSilently, _targetId, _targetId);
                }

                if (_inStartupGrace)
                {
                    _targetId = null;
                    return new PriorityDecision(PriorityOutcome.None, null, null);
                }

                var ended = _overrideId;
                _overrideId = null;
                _targetId = TopLocked(connected);
                return new PriorityDecision(PriorityOutcome.OverrideEnded, _targetId, ended);
            }

            var prev = _targetId;
            var top = TopLocked(connected);
            _targetId = top;

            if (top == prev)
            {
                return new PriorityDecision(
                    top is null ? PriorityOutcome.None : PriorityOutcome.ReassertSilently, top, prev);
            }
            if (top is null)
                return new PriorityDecision(PriorityOutcome.NoneConnected, null, prev);
            if (prev is null || connected.Contains(prev))
                return new PriorityDecision(PriorityOutcome.SwitchedUp, top, prev);

            return new PriorityDecision(PriorityOutcome.FellBack, top, prev);
        }
    }

    /// <summary>
    /// Decides what to do when Windows reports a default-device change to
    /// <paramref name="newDefaultId"/>. <see cref="RestoreDecision.Suspend"/> means
    /// "defer to the debounced reconcile" (the target is gone, or a higher-ranked
    /// device just became the default and the settled reconcile will switch up to it).
    /// </summary>
    /// <param name="isDeviceActive">
    ///   Whether a device is present and active. Called outside the lock because the
    ///   production implementation enumerates COM endpoints.
    /// </param>
    public DefaultChangeDecision EvaluateDefaultChange(string newDefaultId, Func<string, bool> isDeviceActive)
    {
        string? target;
        string? overrideId;
        int? newRank, targetRank;
        lock (_lock)
        {
            target = _targetId;
            overrideId = _overrideId;
            newRank = RankOfLocked(newDefaultId);
            targetRank = target is null ? null : RankOfLocked(target);
        }

        if (IsInternalChange || target is null || newDefaultId == target)
            return new DefaultChangeDecision(RestoreDecision.NoAction, null);
        if (!isDeviceActive(target))
            return new DefaultChangeDecision(RestoreDecision.Suspend, null);
        if (overrideId is null && newRank is { } n && targetRank is { } t && n < t)
            return new DefaultChangeDecision(RestoreDecision.Suspend, null);

        return new DefaultChangeDecision(RestoreDecision.Restore, target);
    }

    /// <summary>
    /// The pick that should be persisted after a flow changes mode: legacy to legacy keeps
    /// the legacy pick, legacy to priority clears it, priority to priority keeps the override,
    /// and priority to legacy pins whatever is currently the default.
    /// </summary>
    public static string? PlanPersistedPick(
        bool wasPriority, bool isPriority, string? legacyPickId, string? overrideId, string? currentDefaultId)
        => (wasPriority, isPriority) switch
        {
            (false, false) => legacyPickId,
            (false, true) => null,
            (true, true) => overrideId,
            (true, false) => currentDefaultId,
        };

    private int? RankOfLocked(string id)
    {
        for (int i = 0; i < _ranked.Count; i++)
            if (_ranked[i].Id == id) return i + 1;
        return null;
    }

    private string? TopLocked(IReadOnlySet<string> connected)
    {
        foreach (var d in _ranked)
            if (connected.Contains(d.Id)) return d.Id;
        return null;
    }
}
