#nullable enable
using System;
using System.Runtime.InteropServices;

namespace AudioLeash;

// Mirrors the ERole enum from mmdeviceapi.h.
// Must match the integer values Windows expects in the COM vtable call.
internal enum ERole
{
    Console        = 0,
    Multimedia     = 1,
    Communications = 2,
}

// ── COM interface: Win7+ ─────────────────────────────────────────────────────
// Method ordering must exactly match the COM vtable layout.
// Methods we never call are stubs that keep the vtable offsets correct.
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat(    [MarshalAs(UnmanagedType.LPWStr)] string dev);
    [PreserveSig] int SetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pmftDefault, IntPtr pmftMin);
    [PreserveSig] int SetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int GetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(   [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// ── COM interface: Vista-era fallback ────────────────────────────────────────
[Guid("568b9108-44bf-40b4-9006-86afe5b5a620")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int SetDeviceFormat(      [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pmftDefault, IntPtr pmftMin);
    [PreserveSig] int SetProcessingPeriod(  [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode(         [MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int GetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetPropertyValue(     [MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFxStore, IntPtr pKey, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(   [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// ── COM-creatable class (CLSID works on Win7–Win11) ──────────────────────────
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

// ── Public wrapper ───────────────────────────────────────────────────────────

/// <summary>
/// Sets the Windows default audio playback endpoint via the undocumented
/// <c>IPolicyConfig</c> COM interface (reverse-engineered; stable since Vista).
/// </summary>
internal sealed class PolicyConfigClient
{
    private readonly IPolicyConfig?      _v7;
    private readonly IPolicyConfigVista? _vista;

    public PolicyConfigClient()
    {
        var com = new CPolicyConfigClient();
        _v7    = com as IPolicyConfig;
        _vista = _v7 is null ? com as IPolicyConfigVista : null;
    }

    /// <summary>
    /// Sets <paramref name="deviceId"/> as the Windows default playback device
    /// for all three roles (Console, Multimedia, Communications), mirroring
    /// what the Windows sound control panel does.
    /// </summary>
    /// <param name="deviceId">
    /// The <c>MMDevice.ID</c> string, e.g. <c>{0.0.0.00000000}.{guid}</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither COM interface is available (audio stack unavailable).
    /// </exception>
    public void SetDefaultEndpoint(string deviceId)
    {
        if (_v7 is not null)
        {
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(_v7.SetDefaultEndpoint(deviceId, ERole.Communications));
            return;
        }

        if (_vista is not null)
        {
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            Marshal.ThrowExceptionForHR(_vista.SetDefaultEndpoint(deviceId, ERole.Communications));
            return;
        }

        throw new InvalidOperationException(
            "Could not obtain IPolicyConfig COM interface. " +
            "The Windows audio stack may be unavailable.");
    }
}
