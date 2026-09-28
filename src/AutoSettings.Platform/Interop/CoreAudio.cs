using System.Runtime.InteropServices;
using AutoSettings.Core.Engine;

namespace AutoSettings.Platform.Interop;

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort reserved1;
    public ushort reserved2;
    public ushort reserved3;
    public IntPtr pointerValue;
    public IntPtr pointerValue2;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClientComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
    [PreserveSig] int Commit();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(ref Guid eventContext);
    [PreserveSig] int VolumeStepDown(ref Guid eventContext);
    [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

/// <summary>
/// Undocumented but long-stable interface used by the Sound control panel to change the default device.
/// Only <see cref="SetDefaultEndpoint"/> is used; the other methods keep the vtable layout.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultFormat, IntPtr format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
}

/// <summary>A playback or recording device.</summary>
internal sealed record AudioDevice(string Id, string Name);

/// <summary>Helpers around the Core Audio API.</summary>
internal static class CoreAudio
{
    private const uint DEVICE_STATE_ACTIVE = 0x1;
    private const uint STGM_READ = 0;
    private const uint CLSCTX_ALL = 0x17;
    private const ushort VT_LPWSTR = 31;
    private static readonly PROPERTYKEY FriendlyNameKey = new() { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    private static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    /// <summary>Lists active devices.</summary>
    public static List<AudioDevice> List(EDataFlow flow)
    {
        var result = new List<AudioDevice>();
        Marshal.ThrowExceptionForHR(Enumerator().EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var collection));
        Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) != 0)
                continue;
            result.Add(Describe(device));
        }
        return result;
    }

    /// <summary>The default device for a role, or null when there is none.</summary>
    public static AudioDevice? Default(EDataFlow flow, ERole role) =>
        Enumerator().GetDefaultAudioEndpoint(flow, role, out var device) == 0 ? Describe(device) : null;

    /// <summary>Finds a device whose name contains <paramref name="nameContains"/>, or the default device when it is empty.</summary>
    public static AudioDevice Find(EDataFlow flow, string? nameContains)
    {
        if (string.IsNullOrWhiteSpace(nameContains))
        {
            return Default(flow, ERole.Multimedia)
                ?? throw new ActionFailedException($"there is no default {(flow == EDataFlow.Render ? "playback" : "recording")} device");
        }

        var devices = List(flow);
        var match = devices.FirstOrDefault(d => string.Equals(d.Name, nameContains, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ActionFailedException(
            $"no {(flow == EDataFlow.Render ? "playback" : "recording")} device matches '{nameContains}'. Available: {string.Join(", ", devices.Select(d => d.Name))}");
    }

    /// <summary>Opens the volume control of a device.</summary>
    public static IAudioEndpointVolume Volume(AudioDevice device)
    {
        Marshal.ThrowExceptionForHR(Enumerator().GetDevice(device.Id, out var mmDevice));
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(mmDevice.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance));
        return (IAudioEndpointVolume)instance;
    }

    /// <summary>Makes <paramref name="deviceId"/> the default device for <paramref name="role"/>.</summary>
    public static void SetDefault(string deviceId, ERole role)
    {
        var policy = (IPolicyConfig)new PolicyConfigClientComObject();
        Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    private static AudioDevice Describe(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var id));
        var name = id;
        if (device.OpenPropertyStore(STGM_READ, out var store) == 0)
        {
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) == 0)
            {
                if (value.vt == VT_LPWSTR && value.pointerValue != IntPtr.Zero)
                    name = Marshal.PtrToStringUni(value.pointerValue) ?? id;
                PropVariantClear(ref value);
            }
        }
        return new AudioDevice(id, name);
    }
}
