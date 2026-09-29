namespace OptiGame.Core.State;

/// <summary>Lit et écrit une famille de réglages (registre, plan d'alimentation…).</summary>
public interface ISettingAccessor
{
    /// <summary>Correspond à <see cref="SettingTarget.Kind"/>.</summary>
    string Kind { get; }

    /// <summary>Renvoie <see cref="SettingValue.Absent"/> si le réglage n'existe pas.</summary>
    SettingValue Read(SettingTarget target);

    /// <summary>Écrire <see cref="SettingValue.Absent"/> supprime la valeur.</summary>
    void Write(SettingTarget target, SettingValue value);
}

public sealed class SettingAccessors
{
    private readonly Dictionary<string, ISettingAccessor> _byKind;

    public SettingAccessors(IEnumerable<ISettingAccessor> accessors)
    {
        _byKind = accessors.ToDictionary(a => a.Kind, StringComparer.OrdinalIgnoreCase);
    }

    public ISettingAccessor For(SettingTarget target) =>
        _byKind.TryGetValue(target.Kind, out var accessor)
            ? accessor
            : throw new InvalidOperationException($"Aucun accesseur pour le type de réglage « {target.Kind} ».");

    public SettingValue Read(SettingTarget target) => For(target).Read(target);

    public void Write(SettingTarget target, SettingValue value) => For(target).Write(target, value);
}
