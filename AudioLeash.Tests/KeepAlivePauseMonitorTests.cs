#nullable enable
using System.Runtime.InteropServices;
using AudioLeash;

namespace AudioLeash.Tests;

/// <summary>
/// Lock/unlock, suspend/resume and display on/off reach the monitor's window as messages.
/// SendMessage on the creating thread calls the window procedure synchronously, so no message
/// loop is needed. Real registration may deliver a genuine display-state message first, so each
/// test clears the collected signals before sending its own.
/// </summary>
public sealed class KeepAlivePauseMonitorTests : IDisposable
{
    private const int WM_WTSSESSION_CHANGE  = 0x02B1;
    private const int WTS_SESSION_LOCK      = 0x7;
    private const int WTS_SESSION_UNLOCK    = 0x8;
    private const int WM_POWERBROADCAST     = 0x0218;
    private const int PBT_APMSUSPEND        = 0x4;
    private const int PBT_APMRESUMEAUTOMATIC = 0x12;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;

    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly List<PauseSignal> _signals = [];
    private readonly KeepAlivePauseMonitor _monitor;

    public KeepAlivePauseMonitorTests()
    {
        _monitor = new KeepAlivePauseMonitor(_signals.Add);
        _signals.Clear();
    }

    public void Dispose() => _monitor.Dispose();

    private IntPtr Send(int msg, int wParam, IntPtr lParam = default) =>
        SendMessage(_monitor.Handle, msg, wParam, lParam);

    /// <summary>Sends PBT_POWERSETTINGCHANGE with a POWERBROADCAST_SETTING holding one uint.</summary>
    private void SendPowerSetting(Guid setting, uint value)
    {
        IntPtr p = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.Copy(setting.ToByteArray(), 0, p, 16);
            Marshal.WriteInt32(p, 16, 4);
            Marshal.WriteInt32(p, 20, (int)value);
            Send(WM_POWERBROADCAST, PBT_POWERSETTINGCHANGE, p);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    [Fact]
    public void SessionLock_RaisesLocked()
    {
        Send(WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK);
        Assert.Equal([PauseSignal.Locked], _signals);
    }

    [Fact]
    public void SessionUnlock_RaisesUnlocked()
    {
        Send(WM_WTSSESSION_CHANGE, WTS_SESSION_UNLOCK);
        Assert.Equal([PauseSignal.Unlocked], _signals);
    }

    [Fact]
    public void PowerBroadcast_Suspend_RaisesSuspending()
    {
        Send(WM_POWERBROADCAST, PBT_APMSUSPEND);
        Assert.Equal([PauseSignal.Suspending], _signals);
    }

    [Fact]
    public void PowerBroadcast_ResumeAutomatic_RaisesResumed()
    {
        Send(WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC);
        Assert.Equal([PauseSignal.Resumed], _signals);
    }

    [Fact]
    public void DisplayState_Off_RaisesDisplayOff()
    {
        SendPowerSetting(GUID_CONSOLE_DISPLAY_STATE, 0);
        Assert.Equal([PauseSignal.DisplayOff], _signals);
    }

    [Fact]
    public void DisplayState_On_RaisesDisplayOn()
    {
        SendPowerSetting(GUID_CONSOLE_DISPLAY_STATE, 1);
        Assert.Equal([PauseSignal.DisplayOn], _signals);
    }

    [Fact]
    public void DisplayState_Dimmed_RaisesDisplayOn()
    {
        SendPowerSetting(GUID_CONSOLE_DISPLAY_STATE, 2);
        Assert.Equal([PauseSignal.DisplayOn], _signals);
    }

    [Fact]
    public void PowerSettingChange_OtherGuid_RaisesNothing()
    {
        SendPowerSetting(Guid.NewGuid(), 0);
        Assert.Empty(_signals);
    }

    [Fact]
    public void PowerBroadcast_ReturnsTrue()
    {
        Assert.Equal((IntPtr)1, Send(WM_POWERBROADCAST, PBT_APMSUSPEND));
    }
}
