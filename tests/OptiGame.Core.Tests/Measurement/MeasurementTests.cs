using OptiGame.Core.Measurement;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Measurement;

public sealed class MeasurementTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    // En-tête des métriques 2.x d'après README-ConsoleApplication.md (v2.6.0), abrégé.
    private const string V2Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode," +
        "FrameType,CPUStartTime,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI," +
        "MsRenderPresentLatency,MsUntilDisplayed,MsPCLatency,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait";

    private static string V2Row(string app, int pid, string swapChain, string betweenPresents, string betweenDisplay) =>
        $"{app},{pid},{swapChain},DXGI,0,512,1,Hardware: Independent Flip,Application,1234.5,NA,{betweenPresents},{betweenDisplay}," +
        "0.1,5.0,6.0,NA,4.0,2.0,1.0,3.0,3.0,0.5";

    [Fact]
    public void Parses_v2_columns_by_name_and_treats_NA_as_missing()
    {
        var csv = string.Join("\n",
            V2Header,
            V2Row("Overwatch.exe", 21784, "0x1A", "6.061", "6.060"),
            V2Row("Overwatch.exe", 21784, "0x1A", "6.2", "NA"));

        var frames = PresentMonCsv.Parse(new StringReader(csv));

        Assert.Equal(2, frames.Count);
        Assert.Equal("Overwatch.exe", frames[0].Application);
        Assert.Equal(21784, frames[0].ProcessId);
        Assert.Equal(6.061, frames[0].MsBetweenPresents, 3);
        Assert.Null(frames[1].MsBetweenDisplayChange);
    }

    [Fact]
    public void Parses_v1_csv_with_different_column_order()
    {
        var csv = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,MsBetweenPresents\n" +
                  "game.exe,10,0x1,DXGI,0,0,0,0.5,16.667\n";

        var frame = Assert.Single(PresentMonCsv.Parse(new StringReader(csv)));

        Assert.Equal(16.667, frame.MsBetweenPresents, 3);
    }

    [Fact]
    public void Rows_without_frame_time_are_skipped_and_decimal_point_is_invariant()
    {
        var csv = string.Join("\n", V2Header, V2Row("g.exe", 1, "0x1", "NA", "NA"), "", V2Row("g.exe", 1, "0x1", "16.5", "16.5"));

        var frame = Assert.Single(PresentMonCsv.Parse(new StringReader(csv)));

        Assert.Equal(16.5, frame.MsBetweenPresents);
    }

    [Fact]
    public void Rejects_non_presentmon_files_and_lists_found_columns()
    {
        var ex = Assert.Throws<FormatException>(() => PresentMonCsv.Parse(new StringReader("a,b,c\n1,2,3")));
        Assert.Contains("Colonnes trouvées : a, b, c", ex.Message);
        Assert.Throws<FormatException>(() => PresentMonCsv.Parse(new StringReader("")));
    }

    [Fact]
    public void Accepts_FrameTime_column_name_of_v2_metrics()
    {
        var csv = "Application,ProcessID,SwapChainAddress,CPUStartTime,FrameTime,CPUBusy,DisplayedTime\n" +
                  "Overwatch.exe,11316,0x1A,12.5,6.25,3.1,6.2\n";

        var frame = Assert.Single(PresentMonCsv.Parse(new StringReader(csv)));

        Assert.Equal(6.25, frame.MsBetweenPresents);
        Assert.Equal(6.2, frame.MsBetweenDisplayChange);
    }

    [Fact]
    public void Main_swap_chain_ignores_overlays()
    {
        var csv = string.Join("\n",
            V2Header,
            V2Row("Overwatch.exe", 1, "0xGAME", "6", "6"),
            V2Row("Overwatch.exe", 1, "0xOVERLAY", "33", "33"),
            V2Row("Overwatch.exe", 1, "0xGAME", "6", "6"),
            V2Row("Overwatch.exe", 1, "0xGAME", "6", "6"));

        var main = PresentMonCsv.MainSwapChain(PresentMonCsv.Parse(new StringReader(csv)));

        Assert.Equal(3, main.Count);
        Assert.All(main, f => Assert.Equal("0xGAME", f.SwapChain));
    }

    [Fact]
    public void Stats_match_hand_computed_values()
    {
        // 99 images à 10 ms + 1 image à 100 ms.
        var frameTimes = Enumerable.Repeat(10.0, 99).Append(100.0).ToList();

        var stats = FrameStats.Compute(frameTimes);

        Assert.Equal(100, stats.FrameCount);
        Assert.Equal(1.09, stats.DurationSeconds, 6);
        Assert.Equal(100 * 1000.0 / 1090, stats.AverageFps, 6); // 91,74 : pondéré par le temps, pas 99,1
        Assert.Equal(10.0, stats.OnePercentLowFps, 6);          // pire 1 % = 1 image à 100 ms
        Assert.Equal(10.0, stats.PointOnePercentLowFps, 6);
        Assert.Equal(10.0, stats.MedianFrameTimeMs, 6);
        Assert.Equal(10.9, stats.P99FrameTimeMs, 6);
        Assert.Equal(100.0, stats.MaxFrameTimeMs);
        Assert.True(stats.IsReliable);
    }

    [Fact]
    public void Low_fps_averages_the_worst_frames()
    {
        // 1000 images : 980 à 5 ms, 20 à 20 ms → pire 1 % (10 images) toutes à 20 ms → 50 FPS.
        var frameTimes = Enumerable.Repeat(5.0, 980).Concat(Enumerable.Repeat(20.0, 20)).ToList();

        var stats = FrameStats.Compute(frameTimes);

        Assert.Equal(50.0, stats.OnePercentLowFps, 6);
        Assert.Equal(50.0, stats.PointOnePercentLowFps, 6);
    }

    [Fact]
    public void Invalid_frame_times_are_ignored_and_empty_capture_is_rejected()
    {
        Assert.Equal(2, FrameStats.Compute([0, -1, double.NaN, 16, 16]).FrameCount);
        Assert.Throws<InvalidOperationException>(() => FrameStats.Compute([0, double.NaN]));
        Assert.False(FrameStats.Compute([16, 16]).IsReliable);
    }

    [Fact]
    public void Comparison_reports_relative_changes()
    {
        var before = new FrameStats(1000, 10, 100, 50, 40, 10, 20, 30);
        var after = new FrameStats(1000, 10, 110, 60, 40, 9, 15, 25);

        var comparison = new FrameStatsComparison(before, after);

        Assert.Equal(10, comparison.AverageFpsChangePercent, 6);
        Assert.Equal(20, comparison.OnePercentLowChangePercent, 6);
        Assert.Equal(0, comparison.PointOnePercentLowChangePercent, 6);
        Assert.Equal(-25, comparison.P99FrameTimeChangePercent, 6);
    }

    [Fact]
    public void PresentMon_arguments_use_verified_options()
    {
        var args = new CaptureRequest(@"C:\t\PresentMon.exe", "Overwatch.exe", 60, 5, @"C:\c\a.csv").BuildArguments();

        Assert.Equal(
        [
            "--process_name", "Overwatch.exe", "--output_file", @"C:\c\a.csv", "--timed", "60", "--terminate_after_timed",
            "--v2_metrics", "--no_console_stats", "--session_name", CaptureRequest.SessionName, "--stop_existing_session",
            "--delay", "5",
        ], args);
        Assert.DoesNotContain("--delay", new CaptureRequest("p", "g.exe", 30, 0, "o.csv").BuildArguments());
    }

    [Fact]
    public void Capture_store_persists_records_and_deletes_csv_on_removal()
    {
        var csvPath = _dir.File("cap.csv");
        File.WriteAllText(csvPath, string.Join("\n", V2Header, V2Row("g.exe", 1, "0x1", "10", "10"), V2Row("g.exe", 1, "0x1", "20", "20")));
        var store = new CaptureStore(new JsonStateStore<CapturesDocument>(_dir.File("captures.json")), _dir.Path);
        var record = new CaptureRecord
        {
            Label = "Avant",
            ProcessName = "g.exe",
            CsvFile = "cap.csv",
            CapturedAt = DateTimeOffset.Now,
            Stats = FrameStats.Compute([10, 20]),
        };
        store.Add(record);

        var reopened = new CaptureStore(new JsonStateStore<CapturesDocument>(_dir.File("captures.json")), _dir.Path);
        var loaded = Assert.Single(reopened.GetAll());
        Assert.Equal("Avant", loaded.Label);
        Assert.Equal(record.Stats, loaded.Stats);
        Assert.Equal([10.0, 20.0], reopened.LoadFrameTimes(loaded));

        reopened.Rename(record.Id, "Avant optimisation");
        Assert.Equal("Avant optimisation", reopened.GetAll()[0].Label);

        reopened.Remove(record.Id);
        Assert.Empty(reopened.GetAll());
        Assert.False(File.Exists(csvPath));
    }
}
