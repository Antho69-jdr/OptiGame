using OptiGame.Core.Changes;
using OptiGame.Core.Settings;
using OptiGame.Core.State;

namespace OptiGame.Core.Diagnostics.Checks;

public sealed class BackgroundRecordingCheck(SettingAccessors settings) : IDiagnosticCheck
{
    public string Id => "windows.background-recording";

    public string Title => "Enregistrement en arrière-plan (Game DVR)";

    public DiagnosticResult Run()
    {
        var historical = settings.Read(KnownSettings.BackgroundRecording);
        var appCapture = settings.Read(KnownSettings.AppCapture);
        var global = settings.Read(KnownSettings.GameDvrEnabled);
        var policy = settings.Read(KnownSettings.GameDvrPolicy);
        var details = new List<string>
        {
            $"HistoricalCaptureEnabled (enregistrement en arrière-plan) = {historical}",
            $"AppCaptureEnabled (captures de jeu) = {appCapture}",
            $"GameDVR_Enabled (interrupteur global) = {global}",
        };
        if (!policy.IsAbsent)
        {
            details.Add($"Stratégie AllowGameDVR = {policy}");
        }

        var blockedByPolicy = policy.AsDWord() == 0;
        var capturesOff = global.AsDWord() == 0 || appCapture.AsDWord() == 0;
        if (historical.AsDWord() is null or 0 || blockedByPolicy || capturesOff)
        {
            return Result(DiagnosticStatus.Ok, "L'enregistrement en arrière-plan est désactivé.", details, []);
        }

        return Result(DiagnosticStatus.NeedsAttention, "L'enregistrement en arrière-plan est activé.", details,
        [
            new DiagnosticFix(new ReversibleChange
            {
                Id = "fix.windows.background-recording",
                Title = "Désactiver l'enregistrement en arrière-plan",
                What = $@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled : {historical} → 0.",
                Why = "Évite l'encodage vidéo permanent des dernières secondes de jeu. Les captures manuelles (Win+Alt+R) restent possibles.",
                Writes = [new SettingWrite(KnownSettings.BackgroundRecording, SettingValue.DWord(0))],
            }),
        ]);
    }

    private DiagnosticResult Result(DiagnosticStatus status, string summary, IReadOnlyList<string> details, IReadOnlyList<DiagnosticFix> fixes) => new()
    {
        CheckId = Id,
        Title = Title,
        Status = status,
        Summary = summary,
        Details = details,
        Fixes = fixes,
        Explanation =
            "La fonction « Enregistrer ce qui s'est passé » de la Game Bar enregistre en continu les dernières " +
            "secondes de jeu : cela occupe l'encodeur vidéo du GPU, de la mémoire et le disque en permanence. La " +
            "désactiver ne supprime pas la Game Bar ni les captures manuelles. Si vous utilisez l'équivalent de votre " +
            "carte graphique (NVIDIA Instant Replay, AMD Replay), il se règle dans son propre logiciel.",
    };
}
