#nullable enable
namespace AudioLeash;

/// <summary>One device row shown in the settings window.</summary>
internal sealed record DeviceRow(string Id, string Name, string? Description, bool IsConnected);

/// <summary>
/// Pure list-editing logic behind the settings window: a ranked list (order = priority)
/// plus the remaining known devices, which stay sorted by name.
/// </summary>
internal sealed class PriorityListEditor
{
    private readonly List<DeviceRow> _ranked = [];
    private readonly List<DeviceRow> _unranked = [];

    public PriorityListEditor(IReadOnlyList<RankedDevice> ranked, IEnumerable<DeviceRow> known)
    {
        var knownById = new Dictionary<string, DeviceRow>(StringComparer.Ordinal);
        foreach (var row in known)
            knownById.TryAdd(row.Id, row); // first one wins

        var rankedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in ranked)
        {
            if (!rankedIds.Add(r.Id)) continue;
            _ranked.Add(knownById.TryGetValue(r.Id, out var current)
                ? current
                : new DeviceRow(r.Id, r.Name, r.Description, IsConnected: false));
        }

        _unranked.AddRange(knownById.Values.Where(k => !rankedIds.Contains(k.Id)));
        SortUnranked();
    }

    public IReadOnlyList<DeviceRow> Ranked => _ranked.ToArray();

    public IReadOnlyList<DeviceRow> Unranked => _unranked.ToArray();

    /// <summary>Moves a ranked row up one slot, or ranks an unranked row at the end. False if nothing moved.</summary>
    public bool MoveUp(string id)
    {
        int i = _ranked.FindIndex(r => r.Id == id);
        if (i > 0)
        {
            (_ranked[i - 1], _ranked[i]) = (_ranked[i], _ranked[i - 1]);
            return true;
        }
        if (i == 0) return false;

        int u = _unranked.FindIndex(r => r.Id == id);
        if (u < 0) return false;
        _ranked.Add(_unranked[u]);
        _unranked.RemoveAt(u);
        return true;
    }

    /// <summary>Moves a ranked row down one slot; the last ranked row becomes unranked. False if nothing moved.</summary>
    public bool MoveDown(string id)
    {
        int i = _ranked.FindIndex(r => r.Id == id);
        if (i < 0) return false;
        if (i < _ranked.Count - 1)
        {
            (_ranked[i + 1], _ranked[i]) = (_ranked[i], _ranked[i + 1]);
            return true;
        }
        _unranked.Add(_ranked[i]);
        _ranked.RemoveAt(i);
        SortUnranked();
        return true;
    }

    /// <summary>
    /// Moves a row to the ranked or unranked list. <paramref name="index"/> is clamped and
    /// ignored for unranked (that list stays sorted). Unknown ids are a no-op.
    /// </summary>
    public void MoveTo(string id, bool toRanked, int index)
    {
        DeviceRow? row = null;
        int i = _ranked.FindIndex(r => r.Id == id);
        if (i >= 0)
        {
            row = _ranked[i];
            _ranked.RemoveAt(i);
        }
        else
        {
            int u = _unranked.FindIndex(r => r.Id == id);
            if (u >= 0)
            {
                row = _unranked[u];
                _unranked.RemoveAt(u);
            }
        }
        if (row is null) return;

        if (toRanked)
        {
            _ranked.Insert(Math.Clamp(index, 0, _ranked.Count), row);
        }
        else
        {
            _unranked.Add(row);
            SortUnranked();
        }
    }

    /// <summary>
    /// Drag-and-drop helper. The display shows <see cref="Ranked"/>, a divider row, then
    /// <see cref="Unranked"/>; <paramref name="gap"/> is the slot just above display row
    /// <paramref name="gap"/> (row count = the end). Gaps up to and including the one just
    /// above the divider rank the row there; any lower gap unranks it.
    /// </summary>
    public void DropAtGap(string id, int gap)
    {
        if (gap > _ranked.Count)
        {
            MoveTo(id, toRanked: false, index: 0);
            return;
        }

        // MoveTo removes a ranked row before inserting, which shifts later slots up by one.
        int current = _ranked.FindIndex(r => r.Id == id);
        MoveTo(id, toRanked: true, index: current >= 0 && current < gap ? gap - 1 : gap);
    }

    public IReadOnlyList<RankedDevice> ToRankedList()
        => _ranked.Select(r => new RankedDevice(r.Id, r.Name, r.Description)).ToArray();

    private void SortUnranked()
    {
        var sorted = _unranked.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(); // stable
        _unranked.Clear();
        _unranked.AddRange(sorted);
    }
}
