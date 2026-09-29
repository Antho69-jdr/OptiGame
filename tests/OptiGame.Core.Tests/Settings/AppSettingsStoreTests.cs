using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Settings;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private AppSettingsStore Open() => new(new JsonStateStore<AppSettings>(_dir.File("settings.json")));

    [Fact]
    public void Defaults_when_missing()
    {
        var settings = Open().Get();

        Assert.Empty(settings.GameFolders);
        Assert.Null(settings.PresentMonPath);
    }

    [Fact]
    public void Updates_are_persisted_and_copies_are_isolated()
    {
        Open().Update(s =>
        {
            s.GameFolders.Add(@"A:\Jeux");
            s.PresentMonPath = @"C:\Outils\PresentMon.exe";
        });

        var loaded = Open().Get();
        Assert.Equal([@"A:\Jeux"], loaded.GameFolders);
        Assert.Equal(@"C:\Outils\PresentMon.exe", loaded.PresentMonPath);

        loaded.GameFolders.Clear();
        Assert.Single(Open().Get().GameFolders);
    }
}
