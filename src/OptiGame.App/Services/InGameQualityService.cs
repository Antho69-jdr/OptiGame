using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core.Changes;
using OptiGame.Core.InGame;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.State;
using OptiGame.Platform;
using OptiGame.Platform.InGame;

namespace OptiGame.App.Services;

/// <summary>Qualité graphique d'un jeu Unreal lue dans son fichier, et le changement d'OptiGame en cours (relus à chaque fois).</summary>
public sealed record InGameQualitySnapshot(InGameSettings? Settings, ChangeRecord? AppliedChange);

/// <summary>
/// Qualité graphique d'un jeu Unreal Engine (GameUserSettings.ini) : application et annulation par le journal des corrections
/// durables (fixes.json), comme le plafond de FPS. Refusées pendant que le jeu tourne : il réécrirait le fichier en quittant.
/// </summary>
public sealed class InGameQualityService([FromKeyedServices(JournalKeys.Fixes)] ChangeJournal fixes, IProcessControl processes)
{
    /// <summary>Fichier et journal : hors du thread UI.</summary>
    public InGameQualitySnapshot Read(GameProfile profile) => new(
        InGameSettingsReader.Read(profile.ExePath),
        fixes.ActiveChanges.FirstOrDefault(c => c.Id == UnrealQuality.ChangeId(profile.Id)));

    public bool IsRunning(GameProfile profile) => processes.FindProcesses(profile.ExePath).Count > 0;

    public void Apply(GameProfile profile, ReversibleChange change)
    {
        EnsureClosed(profile);
        fixes.Apply(change);
    }

    public RestoreReport Undo(GameProfile profile)
    {
        EnsureClosed(profile);
        return fixes.Undo(UnrealQuality.ChangeId(profile.Id));
    }

    private void EnsureClosed(GameProfile profile)
    {
        if (IsRunning(profile))
        {
            throw new GameRunningException($"{profile.Name} est ouvert : fermez-le d'abord, sinon il remplacerait ce réglage en quittant.");
        }
    }
}

/// <summary>Le jeu tourne : son fichier de réglages ne doit pas être modifié.</summary>
public sealed class GameRunningException(string message) : Exception(message);
