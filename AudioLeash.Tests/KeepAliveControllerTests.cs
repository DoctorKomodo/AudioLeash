using AudioLeash;

namespace AudioLeash.Tests;

public class KeepAliveControllerTests
{
    private sealed class FakeStream(string id, Action? onDispose = null) : IKeepAliveStream
    {
        public string DeviceId { get; } = id;
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            onDispose?.Invoke();
        }
    }

    private readonly KeepAliveState _state = new([]);
    private readonly List<FakeStream> _started = [];
    private string? _windowsDefault = "A";
    private bool _retryArmed;
    private bool _failStarts;
    private Func<string, Action<IKeepAliveStream>, FakeStream?>? _startHook;
    private readonly KeepAliveController _controller;

    public KeepAliveControllerTests()
    {
        _controller = new KeepAliveController(_state, () => _windowsDefault, Start, armed => _retryArmed = armed);
    }

    private IKeepAliveStream? Start(string id, Action<IKeepAliveStream> onFaulted)
    {
        if (_failStarts) return null;
        var stream = _startHook?.Invoke(id, onFaulted) ?? new FakeStream(id);
        _started.Add(stream);
        return stream;
    }

    private static RankedDevice D(string id) => new(id, id);

    private IEnumerable<FakeStream> Live => _started.Where(s => !s.Disposed);

    [Fact]
    public void SetEnabled_UsesCurrentWindowsDefault_NotStaleOne()
    {
        _controller.RefreshDefault();   // nothing enabled; the default is A
        _windowsDefault = "B";          // default moves while nothing is enabled (no refresh fires)

        _controller.SetEnabled(D("B"), true);

        Assert.Equal("B", _controller.RunningId);
        Assert.Equal("B", Assert.Single(Live).DeviceId);
    }

    [Fact]
    public void SetEnabled_NonDefaultDevice_StartsNothing()
    {
        _windowsDefault = "B";
        _controller.SetEnabled(D("A"), true);
        Assert.Empty(_started);
    }

    [Fact]
    public void Reconcile_ReenteredDuringStart_LeavesNoOrphanStream()
    {
        _startHook = (id, _) =>
        {
            // A lock signal is delivered while the start blocks the UI thread.
            _startHook = null;
            _state.IsLocked = true;
            _controller.Reconcile();
            return new FakeStream(id);
        };

        _controller.SetEnabled(D("A"), true);

        Assert.Empty(Live);
        Assert.Null(_controller.RunningId);
    }

    [Fact]
    public void Reconcile_ReenteredDuringDispose_LeavesOneLiveStream()
    {
        _startHook = (id, _) => new FakeStream(id, onDispose: () => _controller.Reconcile());
        _controller.SetEnabled(D("A"), true);
        _startHook = null;
        _state.SetEnabled(D("B"), true);
        _windowsDefault = "B";

        _controller.RefreshDefault();

        Assert.Equal("B", Assert.Single(Live).DeviceId);
        Assert.Equal("B", _controller.RunningId);
    }

    [Fact]
    public void Fault_DuringStart_DisposesStreamAndArmsRetry()
    {
        _startHook = (id, onFaulted) =>
        {
            var stream = new FakeStream(id);
            onFaulted(stream); // the playback thread failed before TryStart returned
            return stream;
        };

        _controller.SetEnabled(D("A"), true);

        Assert.Empty(Live);
        Assert.Null(_controller.RunningId);
        Assert.True(_retryArmed);
    }

    [Fact]
    public void Fault_AfterStart_DisposesStreamAndArmsRetry()
    {
        _controller.SetEnabled(D("A"), true);
        var stream = Assert.Single(_started);

        _controller.OnFaulted(stream);

        Assert.True(stream.Disposed);
        Assert.Null(_controller.RunningId);
        Assert.True(_retryArmed);
    }

    [Fact]
    public void Retry_AfterFault_StartsAgain()
    {
        _controller.SetEnabled(D("A"), true);
        _controller.OnFaulted(_started[0]);

        _controller.Reconcile(); // retry timer tick

        Assert.Equal("A", Assert.Single(Live).DeviceId);
        Assert.False(_retryArmed);
    }

    [Fact]
    public void Fault_ForStaleStream_IsIgnored()
    {
        _controller.SetEnabled(D("A"), true);
        var stale = new FakeStream("A");

        _controller.OnFaulted(stale);

        Assert.Equal("A", _controller.RunningId);
        Assert.False(_started[0].Disposed);
        Assert.False(_retryArmed);
    }

    [Fact]
    public void StartFailure_ArmsRetry_AndPauseDisarmsIt()
    {
        _failStarts = true;
        _controller.SetEnabled(D("A"), true);
        Assert.True(_retryArmed);

        _state.IsDisplayOff = true;
        _controller.Reconcile();

        Assert.False(_retryArmed);
    }

    [Fact]
    public void Dispose_StopsTheStream_AndIgnoresLaterCalls()
    {
        _controller.SetEnabled(D("A"), true);

        _controller.Dispose();
        _controller.Reconcile();

        Assert.Empty(Live);
        Assert.Single(_started);
    }
}
