using System.Text;
using OptiGame.Core.InGame;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Platform.InGame;

/// <summary>
/// Valeurs des fichiers .ini des jeux (cible <see cref="KnownSettings.IniValue"/> : Path = « fichier|section », Name = clé).
/// Lecture : Absent si le fichier, la section ou la clé manque. Écriture : seule cette ligne change (<see cref="IniText"/>),
/// même encodage (BOM gardé s'il y en a un), fichier remplacé d'un coup (copie .tmp puis déplacement) ; Absent retire la clé.
/// Un fichier disparu (jeu désinstallé) n'est jamais recréé : l'écriture échoue et le changement reste en attente.
/// </summary>
public sealed class IniFileAccessor : ISettingAccessor
{
    public string Kind => KnownSettings.IniValueKind;

    public SettingValue Read(SettingTarget target)
    {
        var (file, section) = Split(target);
        if (!File.Exists(file)) return SettingValue.Absent;
        var (text, _) = ReadText(file);
        return IniText.Get(text, section, Key(target)) is { } value ? SettingValue.String(value) : SettingValue.Absent;
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var (file, section) = Split(target);
        if (!File.Exists(file)) throw new FileNotFoundException($"Fichier de réglages du jeu introuvable : {file}", file);
        var (text, encoding) = ReadText(file);
        var updated = IniText.Set(text, section, Key(target), value.IsAbsent ? null : value.Text
            ?? throw new ArgumentException($"Valeur .ini invalide : {value}"));
        if (updated == text) return;
        var temp = file + ".optigame.tmp";
        File.WriteAllText(temp, updated, encoding);
        File.Move(temp, file, overwrite: true);
    }

    private static (string File, string Section) Split(SettingTarget target)
    {
        var separator = target.Path.LastIndexOf('|');
        return separator > 0
            ? (target.Path[..separator], target.Path[(separator + 1)..])
            : throw new ArgumentException($"Cible .ini invalide : {target.Path}");
    }

    private static string Key(SettingTarget target) => target.Name ?? throw new ArgumentException("Clé .ini manquante.");

    /// <summary>Texte et encodage du fichier : UTF-8 sans BOM par défaut (GameUserSettings.ini relevés), BOM gardé s'il existe.</summary>
    private static (string Text, Encoding Encoding) ReadText(string file)
    {
        var bytes = File.ReadAllBytes(file);
        Encoding encoding = bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true)
            : bytes is [0xFF, 0xFE, ..] ? new UnicodeEncoding(false, true)
            : bytes is [0xFE, 0xFF, ..] ? new UnicodeEncoding(true, true)
            : new UTF8Encoding(false);
        var preamble = encoding.GetPreamble().Length;
        return (encoding.GetString(bytes, preamble, bytes.Length - preamble), encoding);
    }
}
