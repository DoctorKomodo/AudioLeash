#nullable enable
using System;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>
/// A hidden top-level window that exits the app when Windows ends the session.
/// <para>
/// The Windows Restart Manager — used by the installer to close a running copy before
/// replacing it — and logoff/shutdown both send <c>WM_QUERYENDSESSION</c> then
/// <c>WM_ENDSESSION</c> to an app's top-level windows. A WinForms <c>Form</c> closes itself
/// on these, but AudioLeash runs from an <see cref="ApplicationContext"/> with no form, so
/// without this window nothing exits: the Restart Manager waits ~30 s, gives up, and the
/// installer hangs on "closing applications".
/// </para>
/// </summary>
internal sealed class SessionEndListener : NativeWindow, IDisposable
{
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION      = 0x0016;

    private readonly Action _onSessionEnd;

    /// <param name="onSessionEnd">Called on the UI thread when the session is really ending.</param>
    public SessionEndListener(Action onSessionEnd)
    {
        _onSessionEnd = onSessionEnd;
        // Top-level (not message-only): broadcast session messages only reach top-level windows.
        CreateHandle(new CreateParams { Caption = "AudioLeash" });
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_QUERYENDSESSION:
                m.Result = 1; // never block a logoff or an installer
                return;

            case WM_ENDSESSION:
                // wParam is FALSE when another app cancelled the shutdown; keep running then.
                if (m.WParam != IntPtr.Zero) _onSessionEnd();
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
