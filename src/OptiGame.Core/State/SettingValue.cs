using System.Text.Json.Serialization;

namespace OptiGame.Core.State;

public enum SettingValueKind
{
    /// <summary>La valeur n'existe pas. Restaurer « Absent » = supprimer la valeur.</summary>
    Absent,
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary,
}

/// <summary>Valeur d'un réglage, sérialisable en JSON. Modélisée sur les types du registre.</summary>
public sealed class SettingValue : IEquatable<SettingValue>
{
    [JsonConstructor]
    public SettingValue(SettingValueKind kind, long? number = null, string? text = null, string[]? lines = null, byte[]? bytes = null)
    {
        Kind = kind;
        Number = number;
        Text = text;
        Lines = lines;
        Bytes = bytes;
    }

    public static SettingValue Absent { get; } = new(SettingValueKind.Absent);

    public static SettingValue DWord(uint value) => new(SettingValueKind.DWord, number: value);

    public static SettingValue QWord(ulong value) => new(SettingValueKind.QWord, number: unchecked((long)value));

    public static SettingValue String(string value) => new(SettingValueKind.String, text: value);

    public static SettingValue ExpandString(string value) => new(SettingValueKind.ExpandString, text: value);

    public static SettingValue MultiString(IEnumerable<string> value) => new(SettingValueKind.MultiString, lines: value.ToArray());

    public static SettingValue Binary(byte[] value) => new(SettingValueKind.Binary, bytes: (byte[])value.Clone());

    public SettingValueKind Kind { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Number { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Lines { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? Bytes { get; }

    [JsonIgnore]
    public bool IsAbsent => Kind == SettingValueKind.Absent;

    /// <summary>Valeur DWORD si c'en est une, sinon null (y compris si absente).</summary>
    public uint? AsDWord() => Kind == SettingValueKind.DWord && Number is { } n ? unchecked((uint)n) : null;

    public bool Equals(SettingValue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && Number == other.Number
            && Text == other.Text
            && SequenceEqual(Lines, other.Lines)
            && SequenceEqual(Bytes, other.Bytes);
    }

    public override bool Equals(object? obj) => Equals(obj as SettingValue);

    public override int GetHashCode() => HashCode.Combine(Kind, Number, Text, Lines?.Length, Bytes?.Length);

    public override string ToString() => Kind switch
    {
        SettingValueKind.Absent => "(absent)",
        SettingValueKind.DWord or SettingValueKind.QWord => Number?.ToString() ?? "?",
        SettingValueKind.String or SettingValueKind.ExpandString => $"\"{Text}\"",
        SettingValueKind.MultiString => string.Join(" | ", Lines ?? []),
        SettingValueKind.Binary => Convert.ToHexString(Bytes ?? []),
        _ => Kind.ToString(),
    };

    private static bool SequenceEqual<T>(T[]? a, T[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}
