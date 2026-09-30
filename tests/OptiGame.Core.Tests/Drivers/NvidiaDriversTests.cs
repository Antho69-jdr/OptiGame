using OptiGame.Core.Drivers;

namespace OptiGame.Core.Tests.Drivers;

public sealed class NvidiaDriversTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Drivers", "Samples", name));

    [Theory]
    [InlineData("32.0.15.9186", "591.86")]  // pilote installé sur la machine de dev
    [InlineData("32.0.16.1088", "610.88")]  // pilote proposé par Windows Update
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("32.0.15.612", "506.12")]   // dernier nombre sans zéro de tête
    [InlineData("10.18.13.6200", "362.00")]  // ancien format (Windows 10) : 13 + 6200 → 36200
    [InlineData("32.0.15", null)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void Converts_the_windows_driver_version_to_the_nvidia_version(string? windows, string? expected)
    {
        Assert.Equal(expected, NvidiaDrivers.FromWindowsVersion(windows));
    }

    [Fact]
    public void Compares_nvidia_versions_numerically()
    {
        Assert.True(NvidiaDrivers.Compare("617.14", "591.86") > 0);
        Assert.True(NvidiaDrivers.Compare("610.88", "617.14") < 0);
        Assert.Equal(0, NvidiaDrivers.Compare("617.14", "617.14"));
        Assert.Null(NvidiaDrivers.Compare("617.14", "?"));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", 120, 933)]
    [InlineData("GeForce RTX 3070 Ti", 120, 965)]
    [InlineData("NVIDIA GeForce RTX 3070 Laptop GPU", 123, 939)]
    public void Finds_the_exact_product_in_the_real_product_list(string gpu, int series, int product)
    {
        Assert.Equal((series, product), NvidiaDrivers.FindProduct(gpu, Sample("nvidia-products-excerpt.xml")));
    }

    [Fact]
    public void Unknown_card_is_not_matched_to_a_similar_name()
    {
        Assert.Null(NvidiaDrivers.FindProduct("NVIDIA GeForce RTX 307", Sample("nvidia-products-excerpt.xml")));
        Assert.Null(NvidiaDrivers.FindProduct("AMD Radeon RX 6800", Sample("nvidia-products-excerpt.xml")));
    }

    [Fact]
    public void Reads_the_real_lookup_response()
    {
        var driver = NvidiaDrivers.ParseLookup(Sample("nvidia-lookup-rtx3070.json"));

        Assert.NotNull(driver);
        Assert.Equal("GeForce Game Ready Driver", driver.Name);
        Assert.Equal("617.14", driver.Version);
        Assert.Equal(new DateOnly(2026, 9, 22), driver.ReleaseDate);
        Assert.Equal("https://us.download.nvidia.com/Windows/617.14/617.14-desktop-win10-win11-64bit-international-dch-whql.exe", driver.DownloadUrl.ToString());
        Assert.Equal("990.85 MB", driver.SizeText);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/details/279803/", driver.DetailsUrl?.ToString());
        Assert.Contains("GeForce RTX 30 Series", driver.SupportedSeries);
        Assert.True(NvidiaDrivers.IsOfficialDownload(driver.DownloadUrl));
    }

    [Theory]
    [InlineData("{\"Success\":\"0\"}")]
    [InlineData("{\"Success\":\"1\",\"IDS\":[]}")]
    [InlineData("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Version\":\"617.14\"}}]}")]
    public void Incomplete_responses_give_no_driver(string json)
    {
        Assert.Null(NvidiaDrivers.ParseLookup(json));
    }

    [Theory]
    [InlineData("https://us.download.nvidia.com/Windows/617.14/x.exe", true)]
    [InlineData("https://fr.download.nvidia.com/Windows/617.14/x.exe", true)]
    [InlineData("http://us.download.nvidia.com/Windows/617.14/x.exe", false)]
    [InlineData("https://download.nvidia.com.evil.example/x.exe", false)]
    [InlineData("https://evil.example/download.nvidia.com/x.exe", false)]
    public void Only_official_https_nvidia_downloads_are_allowed(string url, bool allowed)
    {
        Assert.Equal(allowed, NvidiaDrivers.IsOfficialDownload(new Uri(url)));
    }
}
