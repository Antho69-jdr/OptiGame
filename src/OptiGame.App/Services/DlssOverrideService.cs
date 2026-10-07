using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core.Changes;
using OptiGame.Core.Gpu;
using OptiGame.Core.Profiles;
using OptiGame.Core.State;
using OptiGame.Platform;
using OptiGame.Platform.Gpu;

namespace OptiGame.App.Services;

/// <summary>
/// DLSS d'un jeu, relu à chaque fois : bibliothèque livrée avec le jeu, remplacement imposé par le pilote, changement d'OptiGame.
/// </summary>
/// <param name="DriverSupports">Le pilote installé connaît les remplacements DLSS (pilotes récents seulement).</param>
public sealed record DlssSnapshot(bool DriverSupports, DlssLibraryInfo? Library, NvidiaProfileSetting Override, NvidiaProfileSetting Preset,
    ChangeRecord? AppliedChange);

/// <summary>
/// « Modèle DLSS le plus récent » par jeu (<see cref="DlssOverride"/>) : lecture, application et annulation par le journal des
/// corrections durables (fixes.json), comme le plafond de FPS.
/// </summary>
public sealed class DlssOverrideService([FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes)
{
    public static bool IsAvailable => NvidiaProfiles.IsAvailable;

    /// <summary>NVAPI et parcours du dossier du jeu : hors du thread UI.</summary>
    public DlssSnapshot Read(GameProfile profile) => new(
        NvidiaProfiles.Knows(DlssOverride.OverrideId) && NvidiaProfiles.Knows(DlssOverride.PresetId),
        DlssLibrary.Find(profile.ExePath),
        NvidiaProfiles.Read(profile.ExePath, DlssOverride.OverrideId),
        NvidiaProfiles.Read(profile.ExePath, DlssOverride.PresetId),
        fixes.ActiveChanges.FirstOrDefault(c => c.Id == DlssOverride.ChangeId(profile.Id)));

    public void Apply(ReversibleChange change) => fixes.Apply(change);

    public RestoreReport Undo(Guid profileId) => fixes.Undo(DlssOverride.ChangeId(profileId));
}
