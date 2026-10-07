namespace OptiGame.Core.Display;

/// <summary>Un mode d'affichage annoncé par l'écran.</summary>
public sealed record EdidTiming(int Width, int Height, double RefreshHz);

/// <summary>
/// Modes détaillés de l'EDID d'un écran (sa fiche d'identité, HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\…\Device
/// Parameters\EDID) : descripteurs du bloc de base (le 1er = résolution native), de l'extension CTA-861 (balise 0x02) et de
/// l'extension DisplayID (0x70, blocs de modes 0x03 et 0x22). Vérifié le 2026-10-07 sur les EDID réels de la machine de dev :
/// Samsung LC34G55T (3440×1440 à 100 Hz dans le bloc de base, 165 Hz SEULEMENT dans DisplayID), BenQ GW2470 (1080p 60 Hz ;
/// sa plage annoncée va jusqu'à 76 Hz : la plage n'est pas utilisée), Acer KG241Q (1080p 143,85 Hz). Les modes des listes
/// courtes (VIC, modes standard) ne sont pas lus : un mode manqué ne peut que rendre le contrôle plus prudent.
/// </summary>
public static class Edid
{
    public static IReadOnlyList<EdidTiming> Timings(byte[] edid)
    {
        var timings = new List<EdidTiming>();
        if (edid.Length < 128) return timings;
        for (var o = 54; o <= 108; o += 18) AddDetailed(edid, o, timings);
        for (var block = 1; (block + 1) * 128 <= edid.Length; block++)
        {
            var start = block * 128;
            if (edid[start] == 0x02) // CTA-861 : descripteurs détaillés à partir de l'octet d
            {
                int d = edid[start + 2];
                for (var o = start + d; d >= 4 && o + 18 <= start + 127; o += 18)
                {
                    if ((edid[o] | edid[o + 1]) == 0) break;
                    AddDetailed(edid, o, timings);
                }
            }
            else if (edid[start] == 0x70) // DisplayID
            {
                var end = Math.Min(start + 5 + edid[start + 2], start + 127);
                for (var pos = start + 5; pos + 3 <= end;)
                {
                    int tag = edid[pos], length = edid[pos + 2];
                    if (length == 0 && tag == 0) break;
                    if (tag is 0x03 or 0x22) // modes détaillés, 20 octets chacun (horloge en 10 kHz ou en 1 kHz)
                    {
                        for (var t = pos + 3; t + 20 <= pos + 3 + length && t + 20 <= edid.Length; t += 20)
                        {
                            var clock = ((edid[t] | edid[t + 1] << 8 | edid[t + 2] << 16) + 1.0) * (tag == 0x03 ? 10_000 : 1_000);
                            int width = (edid[t + 4] | edid[t + 5] << 8) + 1, hBlank = (edid[t + 6] | edid[t + 7] << 8) + 1;
                            int height = (edid[t + 12] | edid[t + 13] << 8) + 1, vBlank = (edid[t + 14] | edid[t + 15] << 8) + 1;
                            timings.Add(new EdidTiming(width, height, clock / ((width + hBlank) * (double)(height + vBlank))));
                        }
                    }
                    pos += 3 + length;
                }
            }
        }
        return timings;
    }

    /// <summary>Résolution native : celle du 1er mode détaillé (mode préféré de l'écran) ; null s'il n'y en a pas.</summary>
    public static (int Width, int Height)? Native(IReadOnlyList<EdidTiming> timings) =>
        timings.Count > 0 ? (timings[0].Width, timings[0].Height) : null;

    /// <summary>Fréquence la plus haute annoncée à cette résolution, arrondie (143,85 → 144) ; null si aucune.</summary>
    public static int? MaxRefreshAt(IReadOnlyList<EdidTiming> timings, int width, int height)
    {
        var matching = timings.Where(t => t.Width == width && t.Height == height).ToList();
        return matching.Count == 0 ? null : (int)Math.Round(matching.Max(t => t.RefreshHz));
    }

    private static void AddDetailed(byte[] edid, int o, List<EdidTiming> timings)
    {
        var clock = (edid[o] | edid[o + 1] << 8) * 10_000.0; // Hz
        if (clock == 0) return; // descripteur d'affichage (nom, plage…), pas un mode
        int width = edid[o + 2] | (edid[o + 4] & 0xF0) << 4, hBlank = edid[o + 3] | (edid[o + 4] & 0x0F) << 8;
        int height = edid[o + 5] | (edid[o + 7] & 0xF0) << 4, vBlank = edid[o + 6] | (edid[o + 7] & 0x0F) << 8;
        if (width == 0 || height == 0) return;
        timings.Add(new EdidTiming(width, height, clock / ((width + hBlank) * (double)(height + vBlank))));
    }
}
