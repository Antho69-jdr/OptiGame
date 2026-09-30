using OptiGame.Platform.Startup;

namespace OptiGame.Platform.Tests;

public sealed class QuitRequestWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "OptiGame.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string RequestPath => Path.Combine(_dir, QuitRequestWatcher.FileName);

    [Fact]
    public void Request_file_triggers_quit_once_and_is_removed()
    {
        var calls = 0;
        using var signaled = new ManualResetEventSlim();
        using var watcher = new QuitRequestWatcher(_dir, () =>
        {
            Interlocked.Increment(ref calls);
            signaled.Set();
        });

        File.WriteAllText(RequestPath, "1");
        File.AppendAllText(RequestPath, "2"); // plusieurs notifications Windows pour une même demande

        Assert.True(signaled.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(300);
        Assert.Equal(1, calls);
        Assert.False(File.Exists(RequestPath));
    }

    [Fact]
    public void Stale_request_from_a_previous_run_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(RequestPath, "ancienne demande");
        var called = false;

        using var watcher = new QuitRequestWatcher(_dir, () => called = true);
        Thread.Sleep(500);

        Assert.False(called);
        Assert.False(File.Exists(RequestPath));
    }
}
