using OptiGame.Core.Measurement;

namespace OptiGame.Platform.Measurement;

/// <summary>CSV produit par PresentMon → capture enregistrable (page « Mesures » et captures automatiques).</summary>
public static class CaptureReader
{
    /// <exception cref="InvalidOperationException">Aucun fichier, ou aucune image du jeu.</exception>
    /// <exception cref="FormatException">Fichier produit mais non reconnu (à garder pour analyse).</exception>
    public static CaptureRecord Build(CaptureRequest request, string label, string csvName, DateTimeOffset capturedAt, string presentMonOutput,
        string? activeProfile, bool automatic = false, Core.Rating.GraphicsPreset? preset = null)
    {
        if (!File.Exists(request.OutputCsv))
        {
            throw new InvalidOperationException(
                $"PresentMon n'a produit aucun fichier. Le jeu ({request.ProcessName}) était-il lancé pendant la mesure ?\n\n{presentMonOutput}");
        }

        IReadOnlyList<FrameSample> frames;
        using (var reader = new StreamReader(request.OutputCsv))
        {
            frames = PresentMonCsv.MainSwapChain(PresentMonCsv.Parse(reader));
        }
        if (frames.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aucune image capturée pour {request.ProcessName}. Le jeu était-il lancé et affiché pendant la mesure ?");
        }

        return new CaptureRecord
        {
            Label = label,
            ProcessName = request.ProcessName,
            CapturedAt = capturedAt,
            ActiveProfile = activeProfile,
            CsvFile = csvName,
            Stats = FrameStats.Compute(frames.Select(f => f.MsBetweenPresents).ToList()),
            Automatic = automatic,
            Load = FrameLoad.Compute(frames),
            Preset = preset,
        };
    }
}
