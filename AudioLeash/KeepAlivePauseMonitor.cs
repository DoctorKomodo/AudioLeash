#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>A change in one of the conditions that pause keep-alive.</summary>
internal enum PauseSignal { Locked, Unlocked, DisplayOff, DisplayOn, Suspending, Resumed }

/// <summary>
/// A hidden window that reports session lock/unlock, display on/off and suspend/resume, so
/// keep-alive can pause instead of holding the PC awake.
/// <para>
/// Session-change and power-setting notifications are delivered to a window handle
/// (<c>WTSRegisterSessionNotification</c>, <c>RegisterPowerSettingNotification</c>), and suspend/resume
/// arrive as <c>WM_POWERBROADCAST</c>, which is only broadcast to top-level windows — hence a
/// top-level window like <see cref="SessionEndListener"/>. Windows sends the current display
/// state right after registration.
/// </para>
/// </summary>
internal sealed class KeepAlivePauseMonitor : NativeWindow, IDisposable
{
    private const int WM_WTSSESSION_CHANGE   = 0x02B1;
    private const int WTS_SESSION_LOCK       = 0x7;
    private const int WTS_SESSION_UNLOCK     = 0x8;
    private const int WM_POWERBROADCAST      = 0x0218;
    private const int PBT_APMSUSPEND         = 0x4;
    private const int PBT_APMRESUMESUSPEND   = 0x7;
    private const int PBT_APMRESUMEAUTOMATIC = 0x12;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int NOTIFY_FOR_THIS_SESSION     = 0;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private static Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    private readonly Action<PauseSignal> _onSignal;
    private readonly bool _sessionRegistered;
    private IntPtr _displayNotification;

    /// <param name="onSignal">Called on the creating (UI) thread for each change.</param>
    public KeepAlivePauseMonitor(Action<PauseSignal> onSignal)
    {
        _onSignal = onSignal;
        CreateHandle(new CreateParams { Caption = "AudioLeash keep-alive" });

        _sessionRegistered = WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION);
        if (!_sessionRegistered)
            Debug.WriteLine($"WTSRegisterSessionNotification failed: {Marshal.GetLastWin32Error()}");

        _displayNotification = RegisterPowerSettingNotification(
            Handle, ref GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_displayNotification == IntPtr.Zero)
            Debug.WriteLine($"RegisterPowerSettingNotification failed: {Marshal.GetLastWin32Error()}");
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_WTSSESSION_CHANGE:
                switch ((int)m.WParam)
                {
                    case WTS_SESSION_LOCK:   _onSignal(PauseSignal.Locked);   break;
                    case WTS_SESSION_UNLOCK: _onSignal(PauseSignal.Unlocked); break;
                }
                break;

            case WM_POWERBROADCAST:
                switch ((int)m.WParam)
                {
                    case PBT_APMSUSPEND:
                        _onSignal(PauseSignal.Suspending);
                        break;
                    case PBT_APMRESUMESUSPEND:
                    case PBT_APMRESUMEAUTOMATIC:
                        _onSignal(PauseSignal.Resumed);
                        break;
                    case PBT_POWERSETTINGCHANGE when m.LParam != IntPtr.Zero:
                        OnPowerSettingChange(m.LParam);
                        break;
                }
                m.Result = 1;
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Reads a POWERBROADCAST_SETTING: { GUID PowerSetting; DWORD DataLength; UCHAR Data[] }.
    /// For the console display state, Data is a DWORD: 0 = off, 1 = on, 2 = dimmed (treated as on).
    /// </summary>
    private void OnPowerSettingChange(IntPtr setting)
    {
        var guid = Marshal.PtrToStructure<Guid>(setting);
        if (guid != GUID_CONSOLE_DISPLAY_STATE || Marshal.ReadInt32(setting, 16) < 4) return;
        _onSignal(Marshal.ReadInt32(setting, 20) == 0 ? PauseSignal.DisplayOff : PauseSignal.DisplayOn);
    }

    public void Dispose()
    {
        if (_displayNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_displayNotification);
            _displayNotification = IntPtr.Zero;
        }
        if (_sessionRegistered && Handle != IntPtr.Zero)
            WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }
}
