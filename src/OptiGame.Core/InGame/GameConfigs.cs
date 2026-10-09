using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptiGame.Core.Rating;

namespace OptiGame.Core.InGame;

/// <summary>Un réglage lu dans le jeu, tel que son menu le nomme : « Ombres » = « Élevé ».</summary>
public sealed record InGameOption(string Label, string Value);

/// <summary>
/// Définition d'un jeu dans la base décrite par des DONNÉES (Definitions/games.json, ressource de l'assembly) : où est son fichier
/// de réglages, son format, et le sens de chaque valeur, relevé sur le vrai jeu (champ <see cref="Verified"/>). Ajouter un jeu ne
/// demande pas de code, seulement une définition vérifiée.
/// </summary>
/// <param name="Path">Emplacement, avec des repères résolus par Platform : {LocalLow}, {LocalAppData}, {AppData}, {Documents},
/// {ExeDir}.</param>
/// <param name="Format">« json » (valeurs au premier niveau).</param>
public sealed record GameConfigDefinition(string Id, string Name, string Exe, string Path, string Format, string Verified,
    IReadOnlyList<GameConfigSetting> Settings);

/// <summary>Une clé du fichier d'un jeu et son sens.</summary>
/// <param name="Label">Nom affiché (celui du menu du jeu, en français) ; null = clé utilisée seulement pour son rôle.</param>
/// <param name="Role">preset, width, height, displayMode, vsync, frameLimit, upscaler, upscalerMode ; null = simple réglage.</param>
/// <param name="Values">Valeur brute → libellé ; une valeur absente de la liste n'est jamais devinée (« valeur inconnue »).</param>
/// <param name="Presets">preset : valeur brute → réglage d'OptiGame (Low, Medium, High, Ultra) ; absente = aucun (« Personnalisé »).</param>
/// <param name="DisplayModes">displayMode : valeur brute → Fullscreen, Borderless, Windowed.</param>
/// <param name="On">vsync : valeurs brutes qui veulent dire « synchronisation verticale activée ».</param>
/// <param name="Off">upscaler : valeurs brutes qui veulent dire « aucun ».</param>
/// <param name="When">Réglage pris en compte seulement si ces autres clés ont l'une de ces valeurs.</param>
/// <param name="UnlimitedWhen">frameLimit : « sans limite » (0) si ces autres clés ont l'une de ces valeurs.</param>
public sealed record GameConfigSetting(string Key, string? Label = null, string? Role = null, string? Unit = null,
    IReadOnlyDictionary<string, string>? Values = null, IReadOnlyDictionary<string, string>? Presets = null,
    IReadOnlyDictionary<string, string>? DisplayModes = null, IReadOnlyList<string>? On = null, IReadOnlyList<string>? Off = null,
    IReadOnlyDictionary<string, string[]>? When = null, IReadOnlyDictionary<string, string[]>? UnlimitedWhen = null);

public static class GameConfigs
{
    internal sealed record Catalog(int Version, IReadOnlyList<GameConfigDefinition> Games);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<IReadOnlyList<GameConfigDefinition>> Catalogue = new(() =>
    {
        using var stream = typeof(GameConfigs).Assembly.GetManifestResourceStream("OptiGame.Core.InGame.Definitions.games.json")
            ?? throw new InvalidOperationException("Base des réglages des jeux absente de l'assembly.");
        return JsonSerializer.Deserialize<Catalog>(stream, Options)?.Games ?? [];
    });

    /// <summary>Toutes les définitions (base embarquée).</summary>
    public static IReadOnlyList<GameConfigDefinition> All => Catalogue.Value;

    /// <summary>Définition d'un jeu d'après le nom de son exe ; null si OptiGame ne connaît pas ce jeu.</summary>
    public static GameConfigDefinition? For(string exePath) =>
        All.FirstOrDefault(d => d.Exe.Equals(System.IO.Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase));

    /// <summary>Valeurs brutes du fichier (clé → texte) ; FormatException si le fichier n'a pas le format annoncé.</summary>
    public static IReadOnlyDictionary<string, string> ReadValues(string format, string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (format)
        {
            case "json":
                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Fichier JSON sans objet racine.");
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var value = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString(),
                            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                            _ => null,
                        };
                        if (value is not null) values[property.Name] = value;
                    }
                }
                catch (JsonException ex)
                {
                    throw new FormatException($"Fichier JSON illisible : {ex.Message}", ex);
                }
                break;
            default:
                throw new FormatException($"Format de réglages inconnu : {format}.");
        }
        return values;
    }

    /// <summary>
    /// Réglages lus, dans les mots du jeu ; null si rien d'utile. Une valeur inconnue de la définition est montrée comme telle
    /// (« valeur 7, inconnue »), jamais interprétée.
    /// </summary>
    public static InGameSettings? Interpret(GameConfigDefinition definition, IReadOnlyDictionary<string, string> values,
        string sourceName, string sourcePath, DateTime savedAt)
    {
        var options = new List<InGameOption>();
        GraphicsPreset? preset = null;
        string? presetDetail = null;
        int? width = null, height = null, frameLimit = null;
        InGameDisplayMode? displayMode = null;
        bool? vsync = null;
        string? upscaler = null, upscalerMode = null;
        var upscalerOff = false;

        foreach (var setting in definition.Settings)
        {
            if (!values.TryGetValue(setting.Key, out var raw) || !Applies(setting.When, values)) continue;
            var label = setting.Values is null ? Number(raw, setting.Unit) : setting.Values.TryGetValue(raw, out var named) ? named : null;
            switch (setting.Role)
            {
                case "preset":
                    if (setting.Presets?.TryGetValue(raw, out var p) == true && Enum.TryParse<GraphicsPreset>(p, out var known)) preset = known;
                    presetDetail = label is null ? null : $"préréglage « {label} » du jeu";
                    break;
                case "width":
                    width = Int(raw);
                    break;
                case "height":
                    height = Int(raw);
                    break;
                case "displayMode":
                    if (setting.DisplayModes?.TryGetValue(raw, out var m) == true && Enum.TryParse<InGameDisplayMode>(m, out var mode)) displayMode = mode;
                    break;
                case "vsync":
                    if (label is not null) vsync = setting.On?.Contains(raw) == true;
                    break;
                case "frameLimit":
                    frameLimit = Int(raw) is > 0 and var limit ? limit : null;
                    break;
                case "upscaler":
                    upscalerOff = setting.Off?.Contains(raw) == true;
                    upscaler = upscalerOff ? null : label;
                    break;
                case "upscalerMode":
                    upscalerMode = label;
                    break;
            }
            if (setting.Label is not null) options.Add(new InGameOption(setting.Label, label ?? $"valeur {raw}, inconnue"));
        }
        // Limite « sans limite » (Void Crew : VSync = 1) : connue même sans la clé de limite.
        if (frameLimit is null && definition.Settings.FirstOrDefault(s => s.Role == "frameLimit") is { UnlimitedWhen: { } unlimited }
            && Applies(unlimited, values))
        {
            frameLimit = 0;
        }

        var settings = new InGameSettings(definition.Name, sourceName, sourcePath, savedAt, preset, presetDetail, width, height,
            DisplayMode: displayMode, VSync: vsync, FrameLimit: frameLimit,
            Upscaler: upscaler is null ? null : upscalerMode is null ? upscaler : $"{upscaler} {upscalerMode}",
            Options: options);
        return options.Count == 0 && settings.Summary().Length == 0 ? null : settings;
    }

    private static bool Applies(IReadOnlyDictionary<string, string[]>? conditions, IReadOnlyDictionary<string, string> values) =>
        conditions is null || conditions.All(c => values.TryGetValue(c.Key, out var v) && c.Value.Contains(v));

    private static int? Int(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d is >= 0 and < 100_000 ? (int)Math.Round(d) : null;

    private static string? Number(string raw, string? unit) =>
        Int(raw) is { } n ? unit is null ? n.ToString(CultureInfo.InvariantCulture) : $"{n} {unit}" : null;
}
