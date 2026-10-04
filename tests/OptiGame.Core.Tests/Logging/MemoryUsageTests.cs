using OptiGame.Core.Logging;

namespace OptiGame.Core.Tests.Logging;

public sealed class MemoryUsageTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void Separates_the_dotnet_heap_from_the_rest()
    {
        var usage = new MemoryUsage(979 * Mb, 1020 * Mb, 300 * Mb, 450 * Mb, 120 * Mb);
        Assert.Equal(570 * Mb, usage.OutsideGcBytes);
        Assert.Equal("RAM 979 Mo (privée 1020 Mo) · .NET : 300 Mo alloués, 450 Mo réservés, dont gros objets 120 Mo · hors .NET ≈ 570 Mo",
            usage.Describe());
    }

    [Fact]
    public void Never_reports_a_negative_outside_part() =>
        Assert.Equal(0, new MemoryUsage(100 * Mb, 100 * Mb, 150 * Mb, 150 * Mb, 0).OutsideGcBytes);

    [Fact]
    public void Reads_the_current_process()
    {
        var now = MemoryUsage.Now();
        Assert.True(now.WorkingSetBytes > 0);
        Assert.True(now.GcAllocatedBytes > 0);
    }
}
