using System.Globalization;
using OptiGame.Core.Library;

namespace OptiGame.Core.Playtime;

/// <summary>Temps de jeu d'un jeu selon Steam : total (compté par Steam, avant et en dehors d'OptiGame) et dernière partie.</summary>
public sealed record SteamPlaytimeEntry(TimeSpan Total, DateTimeOffset? LastPlayed);

/// <summary>
/// Lecture des fichiers locaux de Steam (lecture seule), formats vérifiés sur la machine de dev le 2026-09-30 :
/// <list type="bullet">
/// <item><c>userdata\&lt;accountid&gt;\config\localconfig.vdf</c> : UserLocalConfigStore > Software > Valve > Steam > apps >
/// &lt;appid&gt; > <c>Playtime</c> (minutes) et <c>LastPlayed</c> (secondes Unix).</item>
/// <item><c>config\loginusers.vdf</c> : users > &lt;SteamID64&gt; > <c>Timestamp</c> (dernière connexion). <c>MostRecent</c>
/// n'est plus écrit par le Steam actuel, mais reste pris en compte s'il est présent.</item>
/// </list>
/// </summary>
public static class SteamPlaytime
{
    /// <summary>SteamID64 = cette base + identifiant de compte (nom du dossier userdata).</summary>
    public const long SteamId64Base = 76561197960265728;

    /// <summary>Temps par appid ; les jeux sans temps enregistré sont ignorés.</summary>
    public static IReadOnlyDictionary<string, SteamPlaytimeEntry> Parse(string localConfigVdf)
    {
        var apps = Vdf.Parse(localConfigVdf)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"];
        var result = new Dictionary<string, SteamPlaytimeEntry>(StringComparer.Ordinal);
        if (apps is null) return result;

        foreach (var (appId, app) in apps.Children)
        {
            var minutes = ParseLong(app.GetString("Playtime"));
            var lastPlayed = ParseLong(app.GetString("LastPlayed")) is > 0 and var seconds
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : (DateTimeOffset?)null;
            if (minutes is not > 0 && lastPlayed is null) continue;
            result[appId] = new SteamPlaytimeEntry(TimeSpan.FromMinutes(minutes ?? 0), lastPlayed);
        }
        return result;
    }

    /// <summary>Identifiant du compte le plus récemment connecté (dossier userdata), ou null.</summary>
    public static string? MostRecentAccountId(string loginUsersVdf)
    {
        var users = Vdf.Parse(loginUsersVdf)["users"];
        if (users is null) return null;

        var candidates = users.Children
            .Select(u => (SteamId: ParseLong(u.Key), MostRecent: u.Value.GetString("MostRecent") == "1", Timestamp: ParseLong(u.Value.GetString("Timestamp")) ?? 0))
            .Where(u => u.SteamId > SteamId64Base)
            .OrderByDescending(u => u.MostRecent)
            .ThenByDescending(u => u.Timestamp)
            .ToList();
        return candidates.Count == 0 ? null : AccountIdFromSteamId64(candidates[0].SteamId!.Value);
    }

    public static string AccountIdFromSteamId64(long steamId64) => (steamId64 - SteamId64Base).ToString(CultureInfo.InvariantCulture);

    private static long? ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>D'où vient le temps de jeu affiché.</summary>
public enum PlaytimeSource
{
    None,
    OptiGame,
    Steam,
}

/// <summary>Temps de jeu affiché : suivi d'OptiGame, complété par Steam quand le jeu en vient.</summary>
public sealed record PlaytimeSummary(TimeSpan Total, DateTimeOffset? LastPlayed, int OptiGameSessions, PlaytimeSource Source)
{
    public static readonly PlaytimeSummary None = new(TimeSpan.Zero, null, 0, PlaytimeSource.None);

    public bool EverPlayed => Source != PlaytimeSource.None;

    /// <summary>
    /// Steam compte aussi les parties suivies par OptiGame : on prend le plus grand des deux totaux (jamais leur somme).
    /// Pendant une partie, Steam n'a pas encore écrit le nouveau total : celui d'OptiGame, qui avance en direct, peut
    /// alors être plus grand. Dernière partie = la plus récente des deux sources.
    /// </summary>
    public static PlaytimeSummary Combine(PlaytimeStats optiGame, SteamPlaytimeEntry? steam)
    {
        var fromOptiGame = optiGame.SessionCount > 0;
        if (steam is null || (steam.Total <= TimeSpan.Zero && steam.LastPlayed is null))
        {
            return fromOptiGame
                ? new PlaytimeSummary(optiGame.Total, optiGame.LastPlayed, optiGame.SessionCount, PlaytimeSource.OptiGame)
                : None;
        }

        var steamWins = steam.Total >= optiGame.Total;
        var last = (steam.LastPlayed, optiGame.LastPlayed) switch
        {
            ({ } s, { } o) => s > o ? s : o,
            ({ } s, null) => s,
            (null, var o) => o,
        };
        return new PlaytimeSummary(steamWins ? steam.Total : optiGame.Total, last, optiGame.SessionCount,
            steamWins ? PlaytimeSource.Steam : PlaytimeSource.OptiGame);
    }
}
