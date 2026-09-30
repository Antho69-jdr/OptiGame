using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Changes;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Platform;

namespace OptiGame.App.Services;

/// <summary>État graphique d'un jeu : entrées de UserGpuPreferences, cartes graphiques et écrans HDR.</summary>
public sealed record GameGraphicsSnapshot(
    IReadOnlyList<GraphicsTarget> Targets,
    GameGraphicsState State,
    IReadOnlyList<string> PhysicalGpus,
    IReadOnlyList<DisplayHdrInfo> Displays,
    ChangeRecord? AppliedChange);

/// <summary>
/// Auto HDR et carte graphique par jeu : lecture de l'état réel (jamais stocké dans le profil, donc jamais désynchronisé)
/// et application par le journal des corrections durables (fixes.json) : annulable ici ou depuis le Diagnostic.
/// </summary>
public sealed class GameGraphicsService(
    [FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes,
    SettingAccessors settings,
    IRegistryReader registry,
    IGpuInfoProvider gpus,
    IDisplayHdrInfo displays)
{
    /// <summary>Lectures registre, WMI et affichage : à appeler hors du thread UI.</summary>
    public GameGraphicsSnapshot Read(GameProfile profile)
    {
        var names = GameGraphics.TargetNames(profile.ExePath, registry.GetValueNames(KnownSettings.GpuPreferencesKey));
        var targets = names.Select(name =>
        {
            var target = KnownSettings.GpuPreference(name);
            var current = settings.Read(target);
            // Valeur d'avant OptiGame : celle du journal si OptiGame a déjà modifié cette entrée.
            var original = fixes.Entries.FirstOrDefault(e => e.Target == target)?.Original ?? current;
            return new GraphicsTarget(name, original, current);
        }).ToList();

        // État affiché : celui de l'entrée existante (Windows en crée parfois plusieurs, identiques).
        var shown = targets.FirstOrDefault(t => !t.Current.IsAbsent)?.Current.Text;
        return new GameGraphicsSnapshot(
            targets,
            GameGraphics.Read(shown),
            gpus.GetAdapters().Where(g => g.IsPhysical).Select(g => g.Name).ToList(),
            displays.GetDisplays(),
            fixes.ActiveChanges.FirstOrDefault(c => c.Id == GameGraphics.ChangeId(profile.Id)));
    }

    public void Apply(ReversibleChange change) => fixes.Apply(change);

    public RestoreReport Undo(Guid profileId) => fixes.Undo(GameGraphics.ChangeId(profileId));
}
