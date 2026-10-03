using System.Globalization;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Platform.Gpu;

/// <summary>
/// Réglages des profils du pilote NVIDIA (cible <see cref="KnownSettings.NvidiaFrameRateLimit"/> : Path = exe, Name = identifiant
/// du réglage en hexadécimal). Absent = non défini dans le profil du jeu ; restaurer Absent retire le réglage et supprime le profil
/// « OptiGame - … » devenu vide. Lecture et écriture vérifiées sans droits administrateur (2026-10-03, exe factice).
/// </summary>
public sealed class NvidiaProfileSettingAccessor : ISettingAccessor
{
    public string Kind => KnownSettings.NvidiaProfileKind;

    public SettingValue Read(SettingTarget target) =>
        NvidiaProfiles.Read(target.Path, SettingId(target)).Value is { } value ? SettingValue.DWord(value) : SettingValue.Absent;

    public void Write(SettingTarget target, SettingValue value) =>
        NvidiaProfiles.Write(target.Path, SettingId(target), value.IsAbsent
            ? null
            : value.AsDWord() ?? throw new ArgumentException($"Valeur de réglage NVIDIA invalide : {value}"));

    private static uint SettingId(SettingTarget target) =>
        target.Name is { } name && name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        uint.TryParse(name[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new ArgumentException($"Identifiant de réglage NVIDIA invalide : {target.Name}");
}
