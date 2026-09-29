using System.Runtime.InteropServices;
using OptiGame.Core.Abstractions;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Gpu;

/// <summary>
/// État réel de HAGS, demandé au noyau graphique (D3DKMTQueryAdapterInfo, KMTQAITYPE_WDDM_2_7_CAPS).
/// Bits : 0 = HwSchSupported, 1 = HwSchEnabled. Les adaptateurs sans nom (ex. Microsoft Basic Render) sont ignorés.
/// </summary>
public sealed class GpuSchedulingProvider : IGpuSchedulingProvider
{
    private const int RegistryInfoStringChars = 260;
    private const int RegistryInfoSize = 4 * RegistryInfoStringChars * sizeof(char);

    public IReadOnlyList<GpuSchedulingState> GetStates()
    {
        var enumeration = new D3dkmtEnumAdapters2();
        if (D3DKMTEnumAdapters2(ref enumeration) != 0 || enumeration.NumAdapters == 0)
        {
            return [];
        }

        var itemSize = Marshal.SizeOf<D3dkmtAdapterInfo>();
        enumeration.pAdapters = Marshal.AllocHGlobal(itemSize * (int)enumeration.NumAdapters);
        var states = new List<GpuSchedulingState>();
        try
        {
            if (D3DKMTEnumAdapters2(ref enumeration) != 0)
            {
                return [];
            }

            for (var i = 0; i < enumeration.NumAdapters; i++)
            {
                var adapter = Marshal.PtrToStructure<D3dkmtAdapterInfo>(enumeration.pAdapters + i * itemSize);
                try
                {
                    var name = QueryName(adapter.hAdapter);
                    var caps = QueryCaps(adapter.hAdapter);
                    if (name is not null && caps is { } c)
                    {
                        states.Add(new GpuSchedulingState(name, Supported: (c & 1) != 0, Enabled: (c & 2) != 0));
                    }
                }
                finally
                {
                    var close = new D3dkmtCloseAdapter { hAdapter = adapter.hAdapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(enumeration.pAdapters);
        }

        return states;
    }

    private static string? QueryName(uint adapter)
    {
        var buffer = Marshal.AllocHGlobal(RegistryInfoSize);
        try
        {
            var query = new D3dkmtQueryAdapterInfo
            {
                hAdapter = adapter,
                Type = KmtqaiTypeAdapterRegistryInfo,
                pPrivateDriverData = buffer,
                PrivateDriverDataSize = RegistryInfoSize,
            };
            return D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint? QueryCaps(uint adapter)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(buffer, 0);
            var query = new D3dkmtQueryAdapterInfo
            {
                hAdapter = adapter,
                Type = KmtqaiTypeWddm27Caps,
                pPrivateDriverData = buffer,
                PrivateDriverDataSize = sizeof(uint),
            };
            return D3DKMTQueryAdapterInfo(ref query) == 0 ? unchecked((uint)Marshal.ReadInt32(buffer)) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
