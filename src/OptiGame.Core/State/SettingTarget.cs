namespace OptiGame.Core.State;

/// <summary>
/// Identifie un réglage de façon sérialisable, pour pouvoir le restaurer même après un crash.
/// <see cref="Kind"/> désigne l'accesseur capable de le lire/écrire (voir <see cref="ISettingAccessor"/>).
/// </summary>
/// <param name="Kind">Type d'accesseur, ex. <c>registry</c>, <c>power-scheme</c>.</param>
/// <param name="Path">Emplacement, ex. <c>HKCU\Software\Microsoft\GameBar</c>.</param>
/// <param name="Name">Nom de la valeur (peut contenir des « \ », ex. chemins d'exe dans UserGpuPreferences).</param>
public sealed record SettingTarget(string Kind, string Path, string? Name = null)
{
    public override string ToString() => Name is null ? $"{Kind}:{Path}" : $"{Kind}:{Path} → {Name}";
}

/// <summary>Une écriture élémentaire : mettre <see cref="Target"/> à <see cref="NewValue"/>.</summary>
public sealed record SettingWrite(SettingTarget Target, SettingValue NewValue);
