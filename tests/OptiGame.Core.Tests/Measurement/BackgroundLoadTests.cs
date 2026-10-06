using OptiGame.Core.Measurement;

namespace OptiGame.Core.Tests.Measurement;

public sealed class BackgroundLoadTests
{
    private const string Game = @"A:\SteamLibrary\steamapps\common\PUBG\TslGame\Binaries\Win64\TslGame.exe";

    private static ProcessCpuSample Sample(int pid, string exe, double cpuSeconds, string? path = null) =>
        new(pid, exe, path ?? $@"C:\Program Files\{exe}", TimeSpan.FromSeconds(cpuSeconds));

    private static bool InWindows(string? path) => path?.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) == true;

    [Fact]
    public void Adds_up_the_processes_of_a_program_and_reports_the_share_of_the_whole_processor()
    {
        // 60 s sur 12 threads = 720 s de processeur au total.
        var before = new[] { Sample(1, "chrome.exe", 10), Sample(2, "chrome.exe", 5), Sample(3, "Discord.exe", 1) };
        var after = new[]
        {
            Sample(1, "chrome.exe", 70),  // +60 s
            Sample(2, "chrome.exe", 32),  // +27 s
            Sample(3, "Discord.exe", 30), // +29 s
            Sample(4, "chrome.exe", 3),   // lancé pendant la mesure : +3 s
        };

        var busy = BackgroundLoad.Summarize(before, after, TimeSpan.FromSeconds(60), 12, Game, InWindows);

        Assert.Equal([("chrome.exe", 12.5, 3), ("Discord.exe", 4.0, 1)], busy.Select(p => (p.ExeName, p.CpuPercent, p.Instances)));
        Assert.Equal("chrome.exe 12,5\u00A0% (3 processus), Discord.exe 4\u00A0%", BackgroundLoad.Describe(busy));
    }

    [Fact]
    public void Ignores_the_game_windows_protected_and_measuring_tools_and_small_loads()
    {
        var after = new[]
        {
            Sample(1, "TslGame.exe", 300),
            Sample(2, "dwm.exe", 60, @"C:\Windows\System32\dwm.exe"),
            Sample(3, "audiodg.exe", 60),          // protégé, même hors du dossier Windows
            Sample(4, "PresentMon-2.6.0-x64.exe", 60),
            Sample(5, "nvidia-smi.exe", 60),
            Sample(6, "Steam.exe", 10),            // 1,4 % : sous le seuil
        };

        Assert.Empty(BackgroundLoad.Summarize([], after, TimeSpan.FromSeconds(60), 12, Game, InWindows));
        Assert.Empty(BackgroundLoad.Summarize([], after, TimeSpan.Zero, 12, Game, InWindows));
    }

    [Fact]
    public void Keeps_the_five_busiest()
    {
        var after = Enumerable.Range(1, 8).Select(i => Sample(i, $"p{i}.exe", 20 * i)).ToList();
        var busy = BackgroundLoad.Summarize([], after, TimeSpan.FromSeconds(60), 12, Game, InWindows);
        Assert.Equal(["p8.exe", "p7.exe", "p6.exe", "p5.exe", "p4.exe"], busy.Select(p => p.ExeName));
    }
}
