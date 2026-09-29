using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Settings;

namespace OptiGame.Core.Tests.Settings;

public sealed class GpuPreferenceStringTests
{
    [Theory]
    [InlineData(null, "GpuPreference=2;")]
    [InlineData("", "GpuPreference=2;")]
    [InlineData("AutoHDREnable=2097;", "AutoHDREnable=2097;GpuPreference=2;")]
    [InlineData("AppStatus=0;AutoHDREnable=2097;", "AppStatus=0;AutoHDREnable=2097;GpuPreference=2;")]
    [InlineData("GpuPreference=1;AutoHDREnable=2097;", "GpuPreference=2;AutoHDREnable=2097;")]
    [InlineData("GpuPreference=1;GpuPreference=0;", "GpuPreference=2;")]
    [InlineData("gpupreference=1", "gpupreference=2;")]
    public void Set_merges_and_preserves_other_keys(string? input, string expected)
    {
        Assert.Equal(expected, GpuPreferenceString.Set(input, GpuPreferenceString.GpuPreferenceKey, "2"));
    }

    [Fact]
    public void Get_reads_value_case_insensitively()
    {
        Assert.Equal("1", GpuPreferenceString.Get("AutoHDREnable=2097;gpupreference=1;", "GpuPreference"));
        Assert.Null(GpuPreferenceString.Get("AutoHDREnable=2097;", "GpuPreference"));
    }

    [Theory]
    [InlineData("3440x1440@165", true, 3440, 1440, 165)]
    [InlineData("1080x1920@60", true, 1080, 1920, 60)]
    [InlineData("3440x1440", false, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0)]
    public void Display_mode_text_round_trips(string? text, bool ok, int w, int h, int hz)
    {
        Assert.Equal(ok, DisplayModeText.TryParse(text, out var width, out var height, out var rate));
        if (ok)
        {
            Assert.Equal((w, h, hz), (width, height, rate));
            Assert.Equal(text, DisplayModeText.Format(width, height, rate));
        }
    }
}
