#nullable enable
using System.Runtime.InteropServices;
using AudioLeash;

namespace AudioLeash.Tests;

/// <summary>
/// The Windows Restart Manager (used by the installer to close a running copy) and logoff
/// send WM_QUERYENDSESSION then WM_ENDSESSION to the app's top-level windows. SendMessage
/// on the creating thread calls the window procedure synchronously, so no message loop is needed.
/// </summary>
public sealed class SessionEndListenerTests : IDisposable
{
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION      = 0x0016;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private int _endCalls;
    private readonly SessionEndListener _listener;

    public SessionEndListenerTests() => _listener = new SessionEndListener(() => _endCalls++);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void QueryEndSession_AllowsTheSessionToEnd()
    {
        IntPtr result = SendMessage(_listener.Handle, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal((IntPtr)1, result);
        Assert.Equal(0, _endCalls);
    }

    [Fact]
    public void EndSession_WhenEnding_InvokesCallbackOnce()
    {
        SendMessage(_listener.Handle, WM_ENDSESSION, (IntPtr)1, IntPtr.Zero);
        Assert.Equal(1, _endCalls);
    }

    [Fact]
    public void EndSession_WhenCancelled_DoesNotInvokeCallback()
    {
        // wParam = FALSE: another app vetoed the shutdown; keep running.
        SendMessage(_listener.Handle, WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(0, _endCalls);
    }
}
