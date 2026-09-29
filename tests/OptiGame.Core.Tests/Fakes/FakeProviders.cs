using OptiGame.Core.Abstractions;

namespace OptiGame.Core.Tests.Fakes;

internal sealed class FakeDisplays(params DisplayInfo[] displays) : IDisplayInfoProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays() => displays;
}

internal sealed class FakeMemory(int? slots, params MemoryModule[] modules) : IMemoryInfoProvider
{
    public MemoryInfo GetMemoryInfo() => new(modules, slots);
}

internal sealed class FakeGpus(params GpuAdapter[] adapters) : IGpuInfoProvider
{
    public IReadOnlyList<GpuAdapter> GetAdapters() => adapters;
}

internal sealed class FakeScheduling(params GpuSchedulingState[] states) : IGpuSchedulingProvider
{
    public IReadOnlyList<GpuSchedulingState> GetStates() => states;
}

internal sealed class FakePowerSchemes(Guid active, params PowerScheme[] schemes) : IPowerSchemeProvider
{
    public Guid GetActiveScheme() => active;

    public IReadOnlyList<PowerScheme> GetSchemes() => schemes;
}

internal sealed class FakePowerStatus(PowerStatus status) : IPowerStatusProvider
{
    public PowerStatus GetStatus() => status;
}

internal sealed class FakeDeviceGuard(DeviceGuardStatus? status) : IDeviceGuardProvider
{
    public DeviceGuardStatus? GetStatus() => status;
}

internal sealed class FakeRegistryReader(Dictionary<string, string[]> valueNames) : IRegistryReader
{
    public IReadOnlyList<string> GetValueNames(string keyPath) => valueNames.GetValueOrDefault(keyPath, []);
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
