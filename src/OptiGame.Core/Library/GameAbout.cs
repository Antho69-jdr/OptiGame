using System.Text.RegularExpressions;

namespace OptiGame.Core.Library;

/// <summary>D'où vient la présentation d'un jeu (carte « À propos du jeu »).</summary>
public enum AboutSource
{
    /// <summary>Magasin Steam, en français (<see cref="SteamStoreAbout"/>).</summary>
    Steam,

    /// <summary>Magasin GOG, demandé en français (certains éditeurs ne l'écrivent qu'en anglais) (<see cref="GogStoreAbout"/>).</summary>
    Gog,

    /// <summary>IGDB, en anglais seulement (<see cref="IgdbAbout"/>) : recours pour les jeux hors Steam et hors GOG.</summary>
    Igdb,
}

/// <summary>Paragraphe de la description d'un jeu (texte seul ; un titre de section est mis en valeur).</summary>
public sealed record GameAboutBlock(string Text, bool IsHeading);

/// <summary>
/// Avis des joueurs sur Steam, toutes langues. <paramref name="Score"/> = review_score de Steam (1 à 9) ; 0 = trop peu d'avis
/// pour un qualificatif (Steam affiche alors « 3 évaluations »).
/// </summary>
public sealed record PlayerReviews(int Score, string Label, int Positive, int Total)
{
    public bool IsRated => Score > 0 && Total > 0;

    /// <summary>Part des avis positifs, arrondie à l'unité la plus proche.</summary>
    public int Percent => Total > 0 ? (int)Math.Round(Positive * 100.0 / Total, MidpointRounding.AwayFromZero) : 0;
}

/// <summary>Note moyenne des joueurs hors Steam : GOG (sur 5, acheteurs vérifiés) ou IGDB (sur 100, votes de ses membres).</summary>
public sealed record CommunityRating(double Value, int Scale, int Count);

/// <summary>
/// Note de la presse sur 100 : publiée par Steam (Metacritic, avec sa page) ou moyenne des critiques relevées par IGDB
/// (<paramref name="Critics"/> = leur nombre ; jamais affichée sous <see cref="IgdbAbout.MinCritics"/>).
/// </summary>
public sealed record PressScore(int Score, string? Url, int? Critics = null);

/// <summary>
/// Bande-annonce publiée sur le magasin Steam : flux HLS en H.264 (décodage matériel le plus répandu) et vignette, toutes deux
/// en https sur *.steamstatic.com.
/// </summary>
public sealed record GameTrailer(string Name, string ThumbnailUrl, string HlsUrl);

/// <summary>
/// Bande-annonce hors Steam, ouverte dans le navigateur (la solution la plus légère) : YouTube (GOG, IGDB) ou le lecteur vidéo
/// de GOG. Seules les adresses de <see cref="WebVideo.Normalize"/> sont gardées.
/// </summary>
public sealed partial record WebVideo(string Name, string Url)
{
    /// <summary>
    /// Adresse ouverte dans le navigateur, ou null si elle n'est pas d'un hébergeur reconnu : https://www.youtube.com/watch?v=&lt;id&gt;
    /// (depuis une adresse « embed » de GOG ou un identifiant IGDB) ou https://fast.wistia.net/embed/iframe/&lt;id&gt; (GOG).
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.Host is "www.youtube.com" or "youtube.com" && YouTubeEmbed().Match(uri.AbsolutePath) is { Success: true } embed)
        {
            return YouTube(embed.Groups[1].Value);
        }
        if (uri.Host == "fast.wistia.net" && WistiaEmbed().IsMatch(uri.AbsolutePath)) return $"https://fast.wistia.net{uri.AbsolutePath}";
        return null;
    }

    /// <summary>Page YouTube d'un identifiant de vidéo (11 caractères) ; null s'il n'en a pas la forme.</summary>
    public static string? YouTube(string? id) =>
        id is not null && YouTubeId().IsMatch(id) ? $"https://www.youtube.com/watch?v={id}" : null;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YouTubeId();

    [GeneratedRegex(@"^/embed/([A-Za-z0-9_-]{11})$")]
    private static partial Regex YouTubeEmbed();

    [GeneratedRegex(@"^/embed/iframe/[A-Za-z0-9]+$")]
    private static partial Regex WistiaEmbed();
}

/// <summary>Présentation d'un jeu sur sa fiche : description, avis des joueurs, note de la presse, bandes-annonces.</summary>
/// <param name="Players">Avis des joueurs sur Steam.</param>
/// <param name="Community">Note des joueurs hors Steam (GOG, IGDB).</param>
/// <param name="Trailers">Bandes-annonces de Steam, lues dans OptiGame.</param>
/// <param name="WebVideos">Bandes-annonces hors Steam, ouvertes dans le navigateur.</param>
public sealed record GameAbout(string Summary, IReadOnlyList<GameAboutBlock> Details, PressScore? Press, PlayerReviews? Players,
    IReadOnlyList<GameTrailer> Trailers, AboutSource Source = AboutSource.Steam, CommunityRating? Community = null,
    IReadOnlyList<WebVideo>? WebVideos = null)
{
    /// <summary>Bandes-annonces ouvertes dans le navigateur (liste vide plutôt que null).</summary>
    public IReadOnlyList<WebVideo> BrowserVideos => WebVideos ?? [];

    /// <summary>Textes en anglais seulement (IGDB n'en a pas d'autres).</summary>
    public bool IsEnglish => Source == AboutSource.Igdb;

    public bool IsEmpty => Summary.Length == 0 && Details.Count == 0 && Press is null && Players is null && Community is null
                           && Trailers.Count == 0 && BrowserVideos.Count == 0;

    /// <summary>
    /// Résumé tiré d'une description sans résumé propre (GOG) : le premier paragraphe, coupé à la fin d'une phrase vers
    /// <paramref name="max"/> caractères ; « » si la description est vide.
    /// </summary>
    public static string SummaryFrom(IReadOnlyList<GameAboutBlock> blocks, int max = 320)
    {
        var first = blocks.FirstOrDefault(b => !b.IsHeading && b.Text.Length >= 40)?.Text ?? blocks.FirstOrDefault(b => !b.IsHeading)?.Text ?? "";
        if (first.Length <= max) return first;
        var cut = first.LastIndexOfAny(['.', '!', '?'], max);
        return cut > max / 2 ? first[..(cut + 1)] : first[..max].TrimEnd() + "…";
    }
}
