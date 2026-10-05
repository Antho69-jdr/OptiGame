using OptiGame.Core.Abstractions;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Core.Tests.Fakes;

namespace OptiGame.Core.Tests.Profiles;

public sealed class ProfileTests : IDisposable
{
    private const string StarCitizen = @"A:\Jeux\Roberts Space Industries\StarCitizen\LIVE\Bin64\StarCitizen.exe";
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private ProfileStore OpenStore() => new(new JsonStateStore<ProfilesDocument>(_dir.File("profiles.json")));

    private static GameProfile Profile(string exe = StarCitizen, params ProcessToClose[] close) => new()
    {
        Name = "Star Citizen",
        ExePath = exe,
        ProcessesToClose = [.. close],
    };

    [Fact]
    public void Profiles_are_persisted_and_returned_as_copies()
    {
        var profile = Profile(close: new ProcessToClose { ExeName = "chrome.exe", Relaunch = true });
        profile.PowerSchemeId = PowerSchemes.HighPerformance;
        profile.Priority = GamePriority.High;
        OpenStore().Save(profile);

        var loaded = Assert.Single(OpenStore().GetAll());
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(PowerSchemes.HighPerformance, loaded.PowerSchemeId);
        Assert.Equal(GamePriority.High, loaded.Priority);
        Assert.True(Assert.Single(loaded.ProcessesToClose).Relaunch);

        loaded.Name = "modifié sans enregistrer";
        Assert.Equal("Star Citizen", OpenStore().GetAll()[0].Name);
    }

    [Fact]
    public void Save_replaces_and_remove_deletes()
    {
        var store = OpenStore();
        var profile = Profile();
        store.Save(profile);
        profile.Name = "SC";
        store.Save(profile);

        Assert.Equal("SC", Assert.Single(store.GetAll()).Name);
        store.Remove(profile.Id);
        Assert.Empty(OpenStore().GetAll());
    }

    [Fact]
    public void Saving_from_a_stale_editor_keeps_artwork_found_meanwhile()
    {
        var store = OpenStore();
        var profile = Profile();
        store.Save(profile);
        var editorCopy = store.Find(profile.Id)!;           // éditeur ouvert sans jaquette
        store.SetArtwork(profile.Id, 42, "co1", "ar1");     // jaquette trouvée pendant l'édition

        editorCopy.Name = "Star Citizen (modifié)";
        store.Save(editorCopy);

        var saved = OpenStore().Find(profile.Id)!;
        Assert.Equal("Star Citizen (modifié)", saved.Name);
        Assert.Equal((42L, "co1", "ar1"), (saved.IgdbGameId!.Value, saved.CoverImageId, saved.HeroImageId));
    }

    [Fact]
    public void SetArtwork_can_clear_and_raises_its_own_event()
    {
        var store = OpenStore();
        var profile = Profile();
        store.Save(profile);
        var changed = 0;
        Guid? artworkFor = null;
        store.Changed += (_, _) => changed++;
        store.ArtworkChanged += (_, id) => artworkFor = id;

        store.SetArtwork(profile.Id, 42, "co1", null);
        store.SetArtwork(profile.Id, null, null, null);

        Assert.Equal(0, changed); // la détection des jeux n'a pas à se relancer
        Assert.Equal(profile.Id, artworkFor);
        Assert.Null(store.Find(profile.Id)!.CoverImageId);
    }

    [Fact]
    public void Custom_background_survives_stale_editors_and_cover_changes()
    {
        var store = OpenStore();
        var profile = Profile();
        store.Save(profile);
        var editorCopy = store.Find(profile.Id)!;
        Guid? artworkFor = null;
        store.ArtworkChanged += (_, id) => artworkFor = id;

        Assert.Null(store.SetCustomHero(profile.Id, "a.jpg"));
        Assert.Equal(profile.Id, artworkFor);
        store.Save(editorCopy);                              // éditeur ouvert avant le choix du fond
        store.SetArtwork(profile.Id, 42, "co1", "ar1");      // nouvelle jaquette : le fond choisi reste
        Assert.Equal("a.jpg", OpenStore().Find(profile.Id)!.CustomHeroFile);

        Assert.Equal("a.jpg", store.SetCustomHero(profile.Id, null)); // ancien fichier rendu, à supprimer
        Assert.Null(OpenStore().Find(profile.Id)!.CustomHeroFile);
        Assert.Equal("b.jpg", store.SetCustomHero(Guid.NewGuid(), "b.jpg")); // jeu retiré entre-temps
    }

    private (ProfileStore Store, GameProfile A, GameProfile B, GameProfile C) ThreeGames()
    {
        var store = OpenStore();
        var a = new GameProfile { Name = "A", ExePath = @"C:\Jeux\a.exe" };
        var b = new GameProfile { Name = "B", ExePath = @"C:\Jeux\b.exe" };
        var c = new GameProfile { Name = "C", ExePath = @"C:\Jeux\c.exe" };
        store.Save(a);
        store.Save(b);
        store.Save(c);
        return (store, a, b, c);
    }

    [Fact]
    public void Pinned_games_are_appended_in_order_and_persisted()
    {
        var (store, a, b, c) = ThreeGames();
        var events = 0;
        store.DockChanged += (_, _) => events++;

        store.SetPinned(c.Id, true);
        store.SetPinned(a.Id, true);
        store.SetPinned(a.Id, true); // déjà épinglé : sans effet

        Assert.Equal(["C", "A"], OpenStore().GetDock().Select(p => p.Name));
        Assert.Equal(2, events);
        Assert.DoesNotContain(store.GetDock(), p => p.Id == b.Id);
    }

    [Fact]
    public void Unpin_and_remove_keep_dock_order_compact()
    {
        var (store, a, b, c) = ThreeGames();
        store.SetPinned(a.Id, true);
        store.SetPinned(b.Id, true);
        store.SetPinned(c.Id, true);

        store.SetPinned(a.Id, false);
        store.Remove(b.Id);

        var dock = OpenStore().GetDock();
        Assert.Equal("C", Assert.Single(dock).Name);
        Assert.Equal(0, dock[0].DockOrder);
    }

    [Fact]
    public void Move_in_dock_reorders_and_clamps()
    {
        var (store, a, b, c) = ThreeGames();
        store.SetPinned(a.Id, true);
        store.SetPinned(b.Id, true);
        store.SetPinned(c.Id, true);

        store.MoveInDock(c.Id, 0);
        Assert.Equal(["C", "A", "B"], store.GetDock().Select(p => p.Name));

        store.MoveInDock(c.Id, 99);
        Assert.Equal(["A", "B", "C"], OpenStore().GetDock().Select(p => p.Name));
    }

    [Fact]
    public void Saving_from_the_editor_keeps_the_dock_position()
    {
        var (store, a, _, _) = ThreeGames();
        var editorCopy = store.Find(a.Id)!;
        store.SetPinned(a.Id, true);

        editorCopy.Name = "A modifié";
        store.Save(editorCopy);

        Assert.Equal(0, store.Find(a.Id)!.DockOrder);
    }

    [Fact]
    public void Exe_matching_ignores_case_and_dot_segments()
    {
        var store = OpenStore();
        store.Save(Profile(@"A:\SteamLibrary\steamapps\common\Scrap Mechanic\.\Release\ScrapMechanic.exe"));

        Assert.NotNull(store.FindEnabledFor(@"a:\steamlibrary\steamapps\common\scrap mechanic\release\scrapmechanic.exe"));
        Assert.Null(store.FindEnabledFor(@"A:\Autre\ScrapMechanic.exe"));
    }

    [Fact]
    public void Disabled_profile_is_not_found_for_detection()
    {
        var store = OpenStore();
        var profile = Profile();
        profile.Enabled = false;
        store.Save(profile);

        Assert.Null(store.FindEnabledFor(StarCitizen));
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("SVCHOST.EXE")]
    [InlineData("optigame.exe")]
    [InlineData("StarCitizen.exe")] // le jeu lui-même
    [InlineData("chrome")]          // pas un .exe
    [InlineData(@"C:\x\chrome.exe")] // chemin au lieu d'un nom
    public void Forbidden_processes_are_rejected(string exeName)
    {
        var ex = Assert.Throws<ProfileValidationException>(() =>
            OpenStore().Save(Profile(close: new ProcessToClose { ExeName = exeName })));

        Assert.Single(ex.Errors);
    }

    [Fact]
    public void Two_profiles_cannot_share_an_exe()
    {
        var store = OpenStore();
        store.Save(Profile());

        Assert.Throws<ProfileValidationException>(() => store.Save(Profile(StarCitizen.ToUpperInvariant())));
    }

    [Fact]
    public void Duplicate_processes_are_rejected()
    {
        Assert.Throws<ProfileValidationException>(() => OpenStore().Save(Profile(close:
        [
            new ProcessToClose { ExeName = "chrome.exe" },
            new ProcessToClose { ExeName = "Chrome.exe" },
        ])));
    }

    [Fact]
    public void Describe_lists_every_action_in_plain_french()
    {
        var profile = Profile(close:
        [
            new ProcessToClose { ExeName = "chrome.exe", Relaunch = true },
            new ProcessToClose { ExeName = "iCUE.exe", Relaunch = false },
        ]);
        profile.PowerSchemeId = PowerSchemes.HighPerformance;
        profile.Priority = GamePriority.AboveNormal;

        var lines = SessionPlan.Describe(profile, [new PowerScheme(PowerSchemes.HighPerformance, "Haute performance")]);

        Assert.Equal(
        [
            "Plan d'alimentation → « Haute performance ».",
            "Fermer chrome.exe, puis le relancer à la fin de la session s'il était ouvert.",
            "Fermer iCUE.exe (pas de relance).",
            "Priorité du jeu : Supérieure à la normale.",
        ], lines);
    }

    [Fact]
    public void Profile_without_power_scheme_produces_no_power_change()
    {
        Assert.Null(SessionPlan.PowerChange(Profile(), []));
    }
}
