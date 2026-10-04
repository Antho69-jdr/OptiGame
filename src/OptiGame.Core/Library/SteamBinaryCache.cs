using System.Buffers.Binary;
using System.Text;

namespace OptiGame.Core.Library;

/// <summary>Nœud KeyValues binaire de Steam : valeur simple (texte, nombre) ou sous-section.</summary>
public sealed class KvNode
{
    public Dictionary<string, KvNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Text { get; init; }

    public long? Number { get; init; }

    public KvNode? this[string key] => Children.GetValueOrDefault(key);

    public string? AsString() => Text ?? Number?.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Une application d'appinfo.vdf : nom, type (« game », « dlc », « tool »…), genres et catégories Steam.</summary>
public sealed record SteamAppInfo(uint AppId, string Name, string Type, IReadOnlyList<int> Genres, IReadOnlyList<int> Categories);

/// <summary>Un paquet (licence) de packageinfo.vdf et les applications qu'il donne.</summary>
public sealed record SteamPackageInfo(uint PackageId, IReadOnlyList<uint> AppIds);

/// <summary>
/// Caches binaires du client Steam (<c>appcache\appinfo.vdf</c> et <c>appcache\packageinfo.vdf</c>). Format NON documenté par
/// Valve, décrit par SteamDB (github.com/SteamDatabase/SteamAppInfo) et vérifié sur les vrais fichiers de la machine de dev le
/// 2026-10-04 : appinfo en version 29 (magic 0x07564429 : clés = index dans une table de noms placée en fin de fichier),
/// packageinfo en version 28 (magic 0x06565528 : clés en texte). Un fichier d'une autre version est refusé (FormatException) plutôt
/// que mal lu. Lecture seule ; on ne modifie jamais ces fichiers.
/// </summary>
public static class SteamBinaryCache
{
    public const uint AppInfoMagic29 = 0x07564429;
    public const uint PackageInfoMagic28 = 0x06565528;

    // Types de valeurs du format KeyValues binaire.
    private const byte TypeSection = 0x00, TypeString = 0x01, TypeInt32 = 0x02, TypeFloat = 0x03, TypePointer = 0x04,
        TypeWide = 0x05, TypeColor = 0x06, TypeUInt64 = 0x07, TypeEnd = 0x08, TypeInt64 = 0x0A, TypeEndAlt = 0x0B;

    public static IReadOnlyList<SteamAppInfo> ParseAppInfo(byte[] data)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != AppInfoMagic29) throw new FormatException($"appinfo.vdf : version non prise en charge (0x{magic:X8}, attendu 0x{AppInfoMagic29:X8}).");
        var tableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8)));
        var names = ReadStringTable(data, tableOffset);

        var apps = new List<SteamAppInfo>();
        var pos = 16;
        while (pos + 8 <= tableOffset)
        {
            var appId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (appId == 0) break;
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
            var next = pos + 8 + size;
            // infoState, lastUpdated, picsToken, SHA1 texte, changeNumber, SHA1 binaire : 60 octets, puis les KeyValues.
            var kvPos = pos + 8 + 60;
            var root = ReadSection(data, ref kvPos, names);
            var common = root["appinfo"]?["common"] ?? root["common"];
            if (common?["name"]?.AsString() is { } name)
            {
                apps.Add(new SteamAppInfo(appId, name, common["type"]?.AsString()?.ToLowerInvariant() ?? "",
                    Ids(common["genres"]), CategoryIds(common["category"])));
            }
            pos = next;
        }
        return apps;
    }

    public static IReadOnlyList<SteamPackageInfo> ParsePackageInfo(byte[] data) =>
        PackageEntries(data).Select(e => new SteamPackageInfo(e.Id, e.AppIds)).ToList();

    /// <summary>Paquets avec leurs bornes dans le fichier (début de l'entrée, fin exclue) : sert aussi à extraire des échantillons.</summary>
    public static IEnumerable<(uint Id, IReadOnlyList<uint> AppIds, int Start, int End)> PackageEntries(byte[] data)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != PackageInfoMagic28) throw new FormatException($"packageinfo.vdf : version non prise en charge (0x{magic:X8}, attendu 0x{PackageInfoMagic28:X8}).");
        var pos = 8; // magic, universe
        while (pos + 4 <= data.Length)
        {
            var start = pos;
            var packageId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (packageId == 0xFFFFFFFF) yield break;
            pos += 4 + 20 + 4 + 8; // identifiant, SHA1, changeNumber, token
            var root = ReadSection(data, ref pos, null);
            var package = root.Children.Values.FirstOrDefault();
            var appIds = package?["appids"]?.Children.Values.Select(v => (uint)(v.Number ?? 0)).Where(id => id != 0).ToList() ?? [];
            yield return (packageId, appIds, start, pos);
        }
    }

    /// <summary>Bornes de chaque application d'appinfo.vdf (début de l'entrée, fin exclue) et position de la table de noms.</summary>
    public static (IReadOnlyList<(uint AppId, int Start, int End)> Entries, int StringTable) AppEntries(byte[] data)
    {
        var tableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8)));
        var entries = new List<(uint, int, int)>();
        var pos = 16;
        while (pos + 8 <= tableOffset)
        {
            var appId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (appId == 0) break;
            var end = pos + 8 + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
            entries.Add((appId, pos, end));
            pos = end;
        }
        return (entries, tableOffset);
    }

    private static List<string> ReadStringTable(byte[] data, int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        var names = new List<string>(count);
        var pos = offset + 4;
        for (var i = 0; i < count; i++) names.Add(ReadCString(data, ref pos));
        return names;
    }

    /// <summary>Lit une section jusqu'à son marqueur de fin. <paramref name="names"/> : table de noms (v29) ou null (clés en texte).</summary>
    private static KvNode ReadSection(byte[] data, ref int pos, List<string>? names)
    {
        var node = new KvNode();
        while (pos < data.Length)
        {
            var type = data[pos++];
            if (type is TypeEnd or TypeEndAlt) return node;
            string key;
            if (names is null)
            {
                key = ReadCString(data, ref pos);
            }
            else
            {
                key = names[BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))];
                pos += 4;
            }
            KvNode value;
            switch (type)
            {
                case TypeSection:
                    value = ReadSection(data, ref pos, names);
                    break;
                case TypeString:
                    value = new KvNode { Text = ReadCString(data, ref pos) };
                    break;
                case TypeInt32 or TypePointer or TypeColor:
                    value = new KvNode { Number = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)) };
                    pos += 4;
                    break;
                case TypeFloat:
                    value = new KvNode { Text = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                    pos += 4;
                    break;
                case TypeUInt64 or TypeInt64:
                    value = new KvNode { Number = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos)) };
                    pos += 8;
                    break;
                case TypeWide:
                    var start = pos;
                    while (pos + 1 < data.Length && (data[pos] != 0 || data[pos + 1] != 0)) pos += 2;
                    value = new KvNode { Text = Encoding.Unicode.GetString(data, start, pos - start) };
                    pos += 2;
                    break;
                default:
                    throw new FormatException($"Type KeyValues inconnu 0x{type:X2} à la position {pos - 1}.");
            }
            node.Children[key] = value;
        }
        throw new FormatException("Fin de fichier au milieu d'une section KeyValues.");
    }

    private static string ReadCString(byte[] data, ref int pos)
    {
        var end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0) throw new FormatException("Chaîne non terminée.");
        var text = Encoding.UTF8.GetString(data, pos, end - pos);
        pos = end + 1;
        return text;
    }

    /// <summary>« genres » : { "0": "1", "1": "23" } → [1, 23].</summary>
    private static List<int> Ids(KvNode? node) =>
        node?.Children.Values.Select(v => int.TryParse(v.AsString(), out var id) ? id : 0).Where(id => id > 0).Distinct().ToList() ?? [];

    /// <summary>« category » : { "category_2": "1", "category_22": "1" } → [2, 22].</summary>
    private static List<int> CategoryIds(KvNode? node) =>
        node?.Children.Keys.Select(k => k.StartsWith("category_", StringComparison.OrdinalIgnoreCase) && int.TryParse(k[9..], out var id) ? id : 0)
            .Where(id => id > 0).ToList() ?? [];
}
