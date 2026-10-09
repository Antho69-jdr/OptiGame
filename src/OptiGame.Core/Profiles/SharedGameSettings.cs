using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiGame.Core.Profiles;

/// <summary>
/// Réglages de partie d'un jeu, à partager par fichier (« .optigame ») : optimiser ou non, plan d'alimentation, priorité,
/// programmes à fermer. RIEN de propre au PC (chemin du jeu, lanceur, identifiants) : le fichier peut être envoyé à n'importe qui.
/// Un fichier reçu vient d'ailleurs : relu strictement (<see cref="Parse"/>), jamais appliqué de lui-même — il remplit l'éditeur
/// de la fiche, l'utilisateur relit puis « Enregistrer ».
/// </summary>
public sealed record SharedGameSettings(string Game, string Exe, bool Optimize, Guid? PowerPlan, GamePriority Priority,
    IReadOnlyList<ProcessToClose> ClosePrograms)
{
    public const string FileExtension = ".optigame";
    public const string Format = "optigame-reglages-de-partie";
    public const int Version = 1;

    /// <summary>Taille maximale d'un fichier accepté (un vrai fichier fait moins de 2 Ko).</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Programmes à fermer gardés au plus.</summary>
    public const int MaxPrograms = 40;

    /// <summary>
    /// Plans d'alimentation de Windows (identifiants identiques sur tous les PC) : seuls ceux-là ont un sens ailleurs. Un plan
    /// personnalisé n'est exporté que s'il est l'un d'eux ; à l'import, un plan absent du PC est ignoré (dit à l'écran).
    /// </summary>
    public static readonly IReadOnlyDictionary<Guid, string> WindowsPowerPlans = new Dictionary<Guid, string>
    {
        [new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")] = "Utilisation normale",
        [new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "Performances élevées",
        [new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")] = "Économie d'énergie",
        [new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "Performances optimales",
    };

    private sealed record FileModel(
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("jeu")] string? Game,
        [property: JsonPropertyName("exe")] string? Exe,
        [property: JsonPropertyName("optimiser")] bool Optimize,
        [property: JsonPropertyName("planAlimentation")] Guid? PowerPlan,
        [property: JsonPropertyName("priorite")] string? Priority,
        [property: JsonPropertyName("programmesAFermer")] IReadOnlyList<ProgramModel>? ClosePrograms);

    private sealed record ProgramModel(
        [property: JsonPropertyName("exe")] string? Exe,
        [property: JsonPropertyName("relancer")] bool Relaunch);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Réglages de partie d'un profil (plan personnalisé non exporté : il n'existe que sur ce PC).</summary>
    public static SharedGameSettings From(GameProfile profile) => new(
        profile.Name, Path.GetFileName(profile.ExePath), profile.Enabled,
        profile.PowerSchemeId is { } plan && WindowsPowerPlans.ContainsKey(plan) ? plan : null,
        profile.Priority,
        profile.ProcessesToClose.Select(p => new ProcessToClose { ExeName = p.ExeName, Relaunch = p.Relaunch }).ToList());

    public string ToJson() => JsonSerializer.Serialize(new FileModel(Format, Version, Game, Exe, Optimize, PowerPlan, Priority.ToString(),
        ClosePrograms.Select(p => new ProgramModel(p.ExeName, p.Relaunch)).ToList()), Options);

    /// <summary>Résultat de la lecture : réglages gardés, et ce qui a été écarté (dit à l'utilisateur).</summary>
    public sealed record ParseResult(SharedGameSettings Settings, IReadOnlyList<string> Ignored);

    /// <summary>
    /// Lit un fichier reçu. FormatException (message en français) s'il n'est pas un fichier de réglages d'OptiGame lisible.
    /// Écartés, sans erreur : programmes protégés (Windows, OptiGame, lanceurs…), noms qui ne sont pas un simple « nom.exe »,
    /// doublons, au-delà de <see cref="MaxPrograms"/>.
    /// </summary>
    public static ParseResult Parse(string json)
    {
        if (json.Length > MaxBytes) throw new FormatException("Fichier trop gros pour des réglages de partie.");
        FileModel? model;
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Ce fichier n'est pas un fichier de réglages d'OptiGame.", ex);
        }
        if (model is null || model.Format != Format) throw new FormatException("Ce fichier n'est pas un fichier de réglages d'OptiGame.");
        if (model.Version > Version) throw new FormatException("Ce fichier vient d'une version plus récente d'OptiGame : mettez OptiGame à jour.");

        var ignored = new List<string>();
        if (!Enum.TryParse<GamePriority>(model.Priority, ignoreCase: false, out var priority) || !Enum.IsDefined(priority))
        {
            if (model.Priority is not null) ignored.Add($"priorité « {model.Priority} » inconnue (gardée : normale)");
            priority = GamePriority.Normal;
        }
        var programs = new List<ProcessToClose>();
        foreach (var program in model.ClosePrograms ?? [])
        {
            var exe = program.Exe?.Trim() ?? "";
            if (!IsPlainExeName(exe))
            {
                ignored.Add($"« {Short(exe)} » n'est pas un nom de programme");
            }
            else if (ProfileValidator.IsProtected(exe))
            {
                ignored.Add($"{exe} : programme protégé, jamais fermé par OptiGame");
            }
            else if (programs.Any(p => p.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            else if (programs.Count >= MaxPrograms)
            {
                ignored.Add($"{exe} : plus de {MaxPrograms} programmes");
            }
            else
            {
                programs.Add(new ProcessToClose { ExeName = exe, Relaunch = program.Relaunch });
            }
        }
        var exeName = IsPlainExeName(model.Exe?.Trim() ?? "") ? model.Exe!.Trim() : "";
        var game = (model.Game ?? "").Trim();
        return new ParseResult(new SharedGameSettings(game.Length > 120 ? game[..120] : game, exeName, model.Optimize, model.PowerPlan, priority, programs), ignored);
    }

    /// <summary>« chrome.exe » : un nom de fichier seul, sans dossier ni caractère interdit.</summary>
    public static bool IsPlainExeName(string name) =>
        name.Length is > 4 and <= 100 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(['\\', '/', ':']) < 0 && name.Trim('.').Length > 4;

    private static string Short(string text) => text.Length > 40 ? text[..40] + "…" : text;

    /// <summary>Nom de fichier proposé à l'export : « Portal 2 - réglages OptiGame.optigame ».</summary>
    public static string SuggestedFileName(string gameName)
    {
        var clean = string.Concat(gameName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return $"{(clean.Length == 0 ? "Jeu" : clean)} - réglages OptiGame{FileExtension}";
    }
}
