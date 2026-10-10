using System.Runtime.InteropServices;

namespace OptiGame.Platform.Audio;

/// <summary>Périphérique audio actif de Windows et son format partagé (« Format par défaut » du panneau Son).</summary>
public sealed record AudioEndpoint(string Name, int SampleRate, int BitsPerSample, int Channels, AudioFormFactor FormFactor);

/// <summary>Forme du périphérique (PKEY_AudioEndpoint_FormFactor, mmdeviceapi.h).</summary>
public enum AudioFormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    Spdif = 8,
    DigitalAudioDisplayDevice = 9,
    Unknown = 10,
}

/// <summary>
/// Micros et sorties actifs, lus par l'API audio de Windows (MMDevice, propriétés du périphérique ; rien n'est ouvert ni
/// enregistré, sans droits administrateur). Le nom est celui que le moteur web affiche pour le micro (PKEY_Device_FriendlyName) :
/// c'est par lui que l'appel retrouve le format d'un micro.
/// </summary>
public static class AudioEndpoints
{
    public static IReadOnlyList<AudioEndpoint> Capture() => List(1);

    public static IReadOnlyList<AudioEndpoint> Render() => List(0);

    /// <summary>Micro par défaut de Windows (rôle « console », celui que le moteur web prend par défaut) ; null s'il n'y en a pas.</summary>
    public static string? DefaultCaptureName() => DefaultName(1);

    /// <summary>Sortie par défaut de Windows ; null s'il n'y en a pas.</summary>
    public static string? DefaultRenderName() => DefaultName(0);

    private static string? DefaultName(int dataFlow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, 0 /* eConsole */, out var device) != 0) return null;
            try
            {
                if (device.OpenPropertyStore(0, out var store) != 0) return null;
                try
                {
                    return ReadString(store, FriendlyName);
                }
                finally
                {
                    Marshal.ReleaseComObject(store);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static IReadOnlyList<AudioEndpoint> List(int dataFlow)
    {
        var result = new List<AudioEndpoint>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(dataFlow, 1 /* DEVICE_STATE_ACTIVE */, out var collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                try
                {
                    if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) continue;
                    try
                    {
                        var name = ReadString(store, FriendlyName) ?? "";
                        var format = ReadBlob(store, DeviceFormat);
                        var formFactor = ReadUInt(store, FormFactorKey) is { } f && f <= 10 ? (AudioFormFactor)f : AudioFormFactor.Unknown;
                        if (format is { Length: >= 16 })
                        {
                            // WAVEFORMATEX : wFormatTag(2) nChannels(2) nSamplesPerSec(4) nAvgBytesPerSec(4) nBlockAlign(2) wBitsPerSample(2)
                            result.Add(new AudioEndpoint(name, BitConverter.ToInt32(format, 4), BitConverter.ToInt16(format, 14),
                                BitConverter.ToInt16(format, 2), formFactor));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(store);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            Marshal.ReleaseComObject(collection);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return result;
    }

    // ---- Propriétés (functiondiscoverykeys_devpkey.h, mmdeviceapi.h) ----

    private static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly PropertyKey DeviceFormat = new(new Guid("f19f064d-082c-4e27-bc73-6882a1bb8e4c"), 0);
    private static readonly PropertyKey FormFactorKey = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    private const ushort VtLpwstr = 31, VtBlob = 65, VtUi4 = 19;

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return null;
        try
        {
            return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static byte[]? ReadBlob(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return null;
        try
        {
            if (value.Type != VtBlob || value.BlobSize == 0 || value.BlobData == IntPtr.Zero) return null;
            var bytes = new byte[value.BlobSize];
            Marshal.Copy(value.BlobData, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static uint? ReadUInt(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return null;
        try
        {
            return value.Type == VtUi4 ? (uint)value.Pointer.ToInt64() : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    // ---- COM (mmdeviceapi.h, propsys.h) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, int id)
    {
        public Guid Format = format;
        public int Id = id;
    }

    /// <summary>PROPVARIANT réduit aux formes lues ici : pointeur (chaîne), entier sur 4 octets, ou blob (taille + données).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public uint BlobSize;
        [FieldOffset(16)] public IntPtr BlobData;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
