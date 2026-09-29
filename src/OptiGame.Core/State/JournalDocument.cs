namespace OptiGame.Core.State;

/// <summary>Contenu d'un fichier journal (<c>session.json</c> ou <c>fixes.json</c>).</summary>
public sealed class JournalDocument
{
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Contexte libre, ex. nom du jeu pour une session.</summary>
    public string? Context { get; set; }

    /// <summary>Dans l'ordre de capture ; la restauration se fait en ordre inverse.</summary>
    public List<JournalEntry> Entries { get; set; } = [];

    public JournalDocument Clone() => new()
    {
        Version = Version,
        CreatedAt = CreatedAt,
        Context = Context,
        Entries = Entries.Select(e => e.Clone()).ToList(),
    };
}

public sealed class JournalEntry
{
    public required SettingTarget Target { get; set; }

    /// <summary>État d'origine, capturé avant la toute première modification de ce réglage. Jamais écrasé.</summary>
    public required SettingValue Original { get; set; }

    /// <summary>Dernière valeur écrite par l'appli (permet de détecter une modification extérieure).</summary>
    public required SettingValue Applied { get; set; }

    /// <summary>Changements qui ont modifié ce réglage.</summary>
    public List<string> ChangeIds { get; set; } = [];

    public DateTimeOffset CapturedAt { get; set; }

    public JournalEntry Clone() => new()
    {
        Target = Target,
        Original = Original,
        Applied = Applied,
        ChangeIds = [.. ChangeIds],
        CapturedAt = CapturedAt,
    };
}
