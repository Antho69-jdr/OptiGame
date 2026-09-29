using OptiGame.Core.State;

namespace OptiGame.Core.Tests.Fakes;

/// <summary>Réglages en mémoire, avec possibilité d'injecter des pannes.</summary>
internal sealed class FakeAccessor(string kind = "fake") : ISettingAccessor
{
    private readonly Dictionary<SettingTarget, SettingValue> _values = [];

    public string Kind { get; } = kind;

    public List<(SettingTarget Target, SettingValue Value)> WriteLog { get; } = [];

    public HashSet<SettingTarget> FailWrites { get; } = [];

    public HashSet<SettingTarget> FailReads { get; } = [];

    /// <summary>Appelé juste avant chaque écriture (sert à simuler un crash à cet instant).</summary>
    public Action<SettingTarget, SettingValue>? BeforeWrite { get; set; }

    public SettingTarget Target(string path, string? name = null) => new(Kind, path, name);

    public void Set(SettingTarget target, SettingValue value)
    {
        if (value.IsAbsent) _values.Remove(target);
        else _values[target] = value;
    }

    public SettingValue Read(SettingTarget target)
    {
        if (FailReads.Contains(target)) throw new IOException($"lecture impossible : {target}");
        return _values.GetValueOrDefault(target, SettingValue.Absent);
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        BeforeWrite?.Invoke(target, value);
        if (FailWrites.Contains(target)) throw new UnauthorizedAccessException($"écriture refusée : {target}");
        WriteLog.Add((target, value));
        Set(target, value);
    }
}
