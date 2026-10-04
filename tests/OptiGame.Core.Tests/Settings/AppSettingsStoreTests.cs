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

    [Fact]
    public void Lightening_during_games_is_on_for_an_older_settings_file()
    {
        // settings.json écrit avant ce réglage : il n'y figure pas.
        File.WriteAllText(_dir.File("settings.json"), """{ "version": 1, "gameFolders": [], "autoMeasureFps": true }""");

        Assert.True(Open().Get().LightDuringGames);
    }

    [Fact]
    public void Lightening_during_games_can_be_turned_off()
    {
        Open().Update(s => s.LightDuringGames = false);

        Assert.False(Open().Get().LightDuringGames);
    }

    [Fact]
    public void Updates_are_automatic_for_an_older_settings_file()
    {
        File.WriteAllText(_dir.File("settings.json"), """{ "version": 1, "gameFolders": [] }""");

        var settings = Open().Get();
        Assert.Equal(UpdateMode.Automatic, settings.UpdateMode);
        Assert.Null(settings.LastRunVersion);
    }

    [Fact]
    public void Update_mode_and_last_run_version_are_persisted()
    {
        Open().Update(s =>
        {
            s.UpdateMode = UpdateMode.Notify;
            s.LastRunVersion = "1.2.0";
        });

        var loaded = Open().Get();
        Assert.Equal(UpdateMode.Notify, loaded.UpdateMode);
        Assert.Equal("1.2.0", loaded.LastRunVersion);
        Assert.Contains("\"Notify\"", File.ReadAllText(_dir.File("settings.json"))); // lisible, comme les autres choix
    }
}
