using System.Text.Json;

namespace OptiGame.Core.Library;

/// <summary>
/// Présentation d'un jeu par IGDB (recours pour les jeux hors Steam et hors GOG ; identifiants de l'utilisateur, seul le numéro du
/// jeu est envoyé). Format relevé le 2026-10-08 (échantillon réel dans les tests) : summary (anglais SEULEMENT), rating /
/// rating_count (votes des membres d'IGDB, sur 100), aggregated_rating / aggregated_rating_count (critiques de la presse, sur 100 ;
/// absents pour Star Citizen, 1 seule critique pour Void Crew), videos [{ name, video_id }] = identifiants YouTube (27 pour
/// Star Citizen).
/// </summary>
public static class IgdbAbout
{
    /// <summary>Note de la presse affichée seulement avec au moins 3 critiques (Void Crew : 1 seule, à 76).</summary>
    public const int MinCritics = 3;

    /// <summary>Note des joueurs affichée seulement avec au moins 10 votes.</summary>
    public const int MinVotes = 10;

    public static string Query(long gameId) =>
        $"fields summary,rating,rating_count,aggregated_rating,aggregated_rating_count,videos.name,videos.video_id; where id = {gameId};";

    /// <summary>Présentation ; null si IGDB ne connaît pas ce numéro ou n'a rien à dire. JsonException si illisible.</summary>
    public static GameAbout? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) return null;
        var game = document.RootElement[0];

        var summary = Text(game, "summary");
        IReadOnlyList<GameAboutBlock> details = summary.Length > 0
            ? summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => new GameAboutBlock(p, false)).ToList()
            : [];
        CommunityRating? community = Number(game, "rating") is { } rating && Count(game, "rating_count") is >= MinVotes and var votes
            ? new CommunityRating(Math.Round(rating), 100, votes)
            : null;
        PressScore? press = Number(game, "aggregated_rating") is { } critics && Count(game, "aggregated_rating_count") is >= MinCritics and var reviews
            ? new PressScore((int)Math.Round(critics), null, reviews)
            : null;

        // Bandes-annonces d'abord (« Trailer », « Launch Trailer »…), puis les autres vidéos.
        var videos = new List<(WebVideo Video, bool Trailer)>();
        if (game.TryGetProperty("videos", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var video in list.EnumerateArray())
            {
                if (WebVideo.YouTube(Text(video, "video_id")) is not { } url) continue;
                var name = Text(video, "name") is { Length: > 0 } n ? n : "Vidéo";
                videos.Add((new WebVideo(name, url), name.Contains("trailer", StringComparison.OrdinalIgnoreCase)));
            }
        }
        var chosen = videos.OrderByDescending(v => v.Trailer).Select(v => v.Video).Take(SteamStoreAbout.MaxTrailers).ToList();
        // Noms en double (« Trailer » trois fois pour Star Citizen) : « Trailer », « Trailer 2 », « Trailer 3 ».
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < chosen.Count; i++)
        {
            var count = seen[chosen[i].Name] = seen.GetValueOrDefault(chosen[i].Name) + 1;
            if (count > 1) chosen[i] = chosen[i] with { Name = $"{chosen[i].Name} {count}" };
        }

        var about = new GameAbout(details.FirstOrDefault()?.Text ?? "", details.Count > 1 ? details : [], press, null, [],
            AboutSource.Igdb, community, chosen);
        return about.IsEmpty ? null : about;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : "";

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && number is > 0 and <= 100 ? number : null;

    private static int Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
