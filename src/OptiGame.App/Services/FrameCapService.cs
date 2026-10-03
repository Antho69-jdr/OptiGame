using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core.Changes;
using OptiGame.Core.Gpu;
using OptiGame.Core.Profiles;
using OptiGame.Core.Settings;
using OptiGame.Core.State;
using OptiGame.Platform;
using OptiGame.Platform.Gpu;

namespace OptiGame.App.Services;

/// <summary>État du plafond de FPS d'un jeu dans le pilote NVIDIA, relu à chaque fois (jamais stocké dans le profil).</summary>
public sealed record FrameCapSnapshot(NvidiaProfileSetting Setting, ChangeRecord? AppliedChange, int RefreshHz);

/// <summary>
/// Plafond de FPS par jeu (profils du pilote NVIDIA) : lecture du profil que le pilote applique au jeu, application et annulation
/// par le journal des corrections durables (fixes.json), comme la carte « Graphismes (Windows) ».
/// </summary>
public sealed class FrameCapService([FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes, GameRatingService ratings)
{
    public static bool IsAvailable => NvidiaProfiles.IsAvailable;

    /// <summary>NVAPI et WMI : hors du thread UI. Lève <see cref="NvidiaApiException"/> si le pilote refuse la lecture.</summary>
    public FrameCapSnapshot Read(GameProfile profile) => new(
        NvidiaProfiles.Read(profile.ExePath, FrameRateCap.SettingId),
        fixes.ActiveChanges.FirstOrDefault(c => c.Id == FrameRateCap.ChangeId(profile.Id)),
        ratings.Pc().RefreshHz);

    public void Apply(ReversibleChange change) => fixes.Apply(change);

    public RestoreReport Undo(Guid profileId) => fixes.Undo(FrameRateCap.ChangeId(profileId));
}
