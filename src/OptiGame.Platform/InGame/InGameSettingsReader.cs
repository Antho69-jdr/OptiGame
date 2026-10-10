using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OptiGame.Core.InGame;

namespace OptiGame.Platform.InGame;

/// <summary>
/// Retrouve et lit les réglages d'un jeu d'après son exe, en lecture seule (rien n'est jamais écrit ici) :
/// <list type="bullet">
/// <item>Jeux de la base de définitions (Core/InGame/Definitions/games.json : Void Crew…) : en premier.</item>
/// <item>Unreal Engine : exe dans &lt;projet&gt;\Binaries\Win64\ (PUBG : TslGame, ARC Raiders : PioneerGame) → GameUserSettings.ini de
/// %LocalAppData%\&lt;projet&gt;\Saved\Config\ (ou Documents\My Games\&lt;projet&gt;\…), le plus récent des dossiers de plateforme.</item>
/// <item>Unity : &lt;exe&gt;_Data\app.info → HKCU\Software\&lt;éditeur&gt;\&lt;jeu&gt;.</item>
/// </list>
/// </summary>
public static class InGameSettingsReader
{
    private static readonly string[] UnrealPlatforms = ["Windows", "WindowsClient", "WindowsNoEditor"];

    /// <summary>Réglages lus, ou null (autre moteur, jeu jamais lancé, fichier illisible).</summary>
    public static InGameSettings? Read(string exePath)
    {
        try
        {
            // Détection automatique en dernier : seulement pour un jeu sans définition ni moteur reconnu.
            return ReadDefined(exePath) ?? ReadUnreal(exePath) ?? ReadUnity(exePath) ?? GameConfigFinder.Read(exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Jeu de la base de définitions (<see cref="GameConfigs"/>) : son fichier, au chemin de la définition, interprété par elle. Un
    /// fichier qui n'a pas le format annoncé (mise à jour du jeu) est ignoré, jamais lu « à peu près ».
    /// </summary>
    private static InGameSettings? ReadDefined(string exePath)
    {
        if (GameConfigs.For(exePath) is not { } definition || DefinitionPath(definition.Path, exePath) is not { } path || !File.Exists(path)) return null;
        try
        {
            var values = GameConfigs.ReadValues(definition.Format, File.ReadAllText(path));
            return GameConfigs.Interpret(definition, values, Path.GetFileName(path), path, File.GetLastWriteTime(path));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Chemin d'une définition, repères remplacés ; null si un repère est inconnu.</summary>
    public static string? DefinitionPath(string template, string exePath)
    {
        var folders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["{LocalLow}"] = LocalLow(),
            ["{LocalAppData}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["{AppData}"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["{Documents}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["{ExeDir}"] = Path.GetDirectoryName(exePath),
        };
        var start = template.IndexOf('{');
        var end = template.IndexOf('}');
        if (start != 0 || end < 0 || !folders.TryGetValue(template[..(end + 1)], out var root) || string.IsNullOrEmpty(root)) return null;
        return Path.GetFullPath(root + template[(end + 1)..]);
    }

    /// <summary>%UserProfile%\AppData\LocalLow, demandé à Windows (dossier connu LocalAppDataLow).</summary>
    internal static string? LocalLow()
    {
        var id = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Réglage du jeu au moment d'une mesure : celui choisi dans OptiGame, sinon celui lu dans le jeu (noté avec la capture).</summary>
    public static Core.Rating.GraphicsPreset? PresetForCapture(Core.Profiles.GameProfile? profile) =>
        profile is null ? null : profile.GraphicsPreset ?? Read(profile.ExePath)?.Preset;

    /// <summary>Nom du projet Unreal : dossier au-dessus de Binaries\Win64 ; null si l'exe n'est pas rangé ainsi.</summary>
    public static string? UnrealProject(string exePath)
    {
        var win64 = Path.GetDirectoryName(exePath);
        var binaries = Path.GetDirectoryName(win64);
        if (win64 is null || binaries is null) return null;
        if (!Path.GetFileName(win64).Equals("Win64", StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(binaries).Equals("Binaries", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return Path.GetFileName(Path.GetDirectoryName(binaries));
    }

    /// <summary>Fichiers GameUserSettings.ini possibles du projet, du plus récent au plus ancien.</summary>
    public static IReadOnlyList<FileInfo> UnrealFiles(string project)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), project),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", project),
        };
        return roots
            .SelectMany(root => UnrealPlatforms.Select(platform => new FileInfo(Path.Combine(root, "Saved", "Config", platform, UnrealSettings.FileName))))
            .Where(f => f.Exists)
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    private static InGameSettings? ReadUnreal(string exePath)
    {
        if (UnrealProject(exePath) is not { Length: > 0 } project) return null;
        foreach (var file in UnrealFiles(project))
        {
            if (UnrealSettings.Parse(File.ReadAllText(file.FullName), file.FullName, file.LastWriteTime, project) is { } settings) return settings;
        }
        return null;
    }

    private static InGameSettings? ReadUnity(string exePath)
    {
        var dataFolder = Path.Combine(Path.GetDirectoryName(exePath) ?? "", Path.GetFileNameWithoutExtension(exePath) + "_Data");
        var appInfo = Path.Combine(dataFolder, UnitySettings.AppInfoFile);
        if (!File.Exists(appInfo) || UnitySettings.ParseAppInfo(File.ReadAllText(appInfo)) is not { } names) return null;

        var keyPath = $@"Software\{names.Company}\{names.Product}";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
        if (key is null) return null;
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in key.GetValueNames())
        {
            if (name.StartsWith("Screenmanager", StringComparison.Ordinal) && key.GetValue(name) is int value) values[name] = value;
        }
        return UnitySettings.FromRegistry(values, $@"HKCU\{keyPath}", LastWriteTime(key) ?? File.GetLastWriteTime(appInfo));
    }

    /// <summary>Date de dernière écriture d'une clé de registre (RegQueryInfoKey), en heure locale.</summary>
    private static DateTime? LastWriteTime(RegistryKey key)
    {
        var status = RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var written);
        return status == 0 ? DateTime.FromFileTime(written) : null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryInfoKey(SafeRegistryHandle key, IntPtr className, IntPtr classLength, IntPtr reserved,
        IntPtr subKeys, IntPtr maxSubKeyLength, IntPtr maxClassLength, IntPtr values, IntPtr maxValueNameLength,
        IntPtr maxValueLength, IntPtr securityDescriptor, out long lastWriteTime);
}
