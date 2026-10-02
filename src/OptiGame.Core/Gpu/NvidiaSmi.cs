using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace OptiGame.Core.Gpu;

/// <summary>Mémoire d'une carte NVIDIA : vidéo (FB) et fenêtre BAR1 visible par le processeur, en Mio (null = illisible).</summary>
public sealed record NvidiaGpuMemory(string Name, string? Architecture, int? VramMib, int? Bar1Mib);

/// <summary>
/// Lecture de « nvidia-smi -q -x » (outil installé avec le pilote, %SystemRoot%\System32\nvidia-smi.exe). Format vérifié sur le
/// pilote 617.14 le 2026-10-03 (extrait dans les tests) : &lt;gpu&gt; → product_name, product_architecture,
/// fb_memory_usage/total et bar1_memory_usage/total (« 8192 MiB »). La DOCTYPE pointe vers un fichier .dtd : ignorée.
/// </summary>
public static class NvidiaSmi
{
    public static IReadOnlyList<string> QueryArguments { get; } = ["-q", "-x"];

    public static IReadOnlyList<NvidiaGpuMemory> ParseMemory(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var document = XDocument.Load(reader);
        return document.Root?.Elements("gpu").Select(gpu => new NvidiaGpuMemory(
                ((string?)gpu.Element("product_name"))?.Trim() ?? "NVIDIA",
                ((string?)gpu.Element("product_architecture"))?.Trim(),
                Mib((string?)gpu.Element("fb_memory_usage")?.Element("total")),
                Mib((string?)gpu.Element("bar1_memory_usage")?.Element("total"))))
            .ToList() ?? [];
    }

    /// <summary>« 8192 MiB » → 8192 ; « N/A » → null.</summary>
    private static int? Mib(string? text)
    {
        var number = text?.Trim().Split(' ')[0];
        return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
