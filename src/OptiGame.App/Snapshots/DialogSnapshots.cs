using System.Windows;
using OptiGame.App.Controls;
using OptiGame.App.Dialogs;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Artwork;
using OptiGame.Core.Changes;
using OptiGame.Core.Drivers;
using OptiGame.Core.Library;

namespace OptiGame.App.Snapshots;

/// <summary>
/// Dialogues remplis de données d'exemple, pour le mode --snapshot (rendu en PNG, jamais ouverts en modal ni validés).
/// Les textes reprennent ceux de l'appli ; aucune donnée réelle n'est lue ni écrite.
/// </summary>
internal static class DialogSnapshots
{
    public static IEnumerable<(string Name, Func<Window> Create)> All()
    {
        yield return ("7-dialogue-question", () => MessageDialog.Question("Retirer Portal 2 de Mes jeux ?",
            "Seuls ses réglages OptiGame sont supprimés : le jeu reste installé et aucun réglage de Windows n'est modifié. " +
            "Vous pourrez le rajouter plus tard.", "Retirer le jeu", isDestructive: true));

        yield return ("7-dialogue-erreur", () =>
        {
            var dialog = MessageDialog.Notice(DialogIcon.Error, "Optimisation non appliquée",
                "« Désactiver l'enregistrement en arrière-plan » n'a pas pu être appliquée : rien n'a été modifié.",
                "System.UnauthorizedAccessException: Access to the registry key 'HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' is denied.");
            if (dialog.FindName("DetailsExpander") is System.Windows.Controls.Expander expander) expander.IsExpanded = true;
            return dialog;
        });

        var change = new ReversibleChange
        {
            Id = "fix.vbs.hvci",
            Title = "Désactiver l'intégrité de la mémoire",
            What = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled = 0",
            Why = "L'intégrité de la mémoire (HVCI) peut coûter quelques pour cent de performances dans certains jeux.",
            Warning = "Elle protège Windows contre l'injection de code dans le noyau : la désactiver réduit la sécurité du PC.",
            RequiresAdmin = true,
            RequiresReboot = true,
            Writes = [],
        };
        yield return ("7-dialogue-optimisation-avancee", () => new ConfirmChangeDialog(change, isAdvanced: true));

        yield return ("7-dialogue-optimisations", () => new ConfirmChangesDialog(
        [
            change with { Id = "fix.a", Title = "Désactiver l'enregistrement en arrière-plan", Warning = null, RequiresReboot = false,
                Why = "La Game Bar enregistre en continu les dernières minutes de jeu, ce qui coûte des performances.",
                What = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled = 0" },
            change with { Id = "fix.b", Title = "Activer le Mode Jeu", Warning = null, RequiresAdmin = false, RequiresReboot = false,
                Why = "Windows donne la priorité au jeu et suspend les mises à jour pendant la partie.",
                What = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = 1" },
            change with { Id = "fix.c", Title = "Plan d'alimentation Performances élevées", Warning = null, RequiresReboot = false,
                Why = "Le processeur garde des fréquences élevées pendant les parties.", What = "Plan actif = Performances élevées" },
        ]));

        yield return ("7-dialogue-pilote", () => new DriverInstallDialog(new DriverInstallPlan(
            "Installer le pilote NVIDIA 617.14", "NVIDIA GeForce RTX 3070\n591.86 → 617.14 (GeForce Game Ready, 22/09/2026)",
            "Version plus récente publiée par NVIDIA : corrections et optimisations pour les jeux récents.", MayRequireReboot: true,
            DriverInstallPlans.NotReversible,
            "Pour revenir au pilote précédent : Gestionnaire de périphériques → clic droit sur la carte → Propriétés → onglet Pilote → « Restaurer le pilote ».",
            RestorePointAvailability.Available)));

        yield return ("7-dialogue-jeux-installes", () => new GameScanDialog(
        [
            new InstalledGame("Portal 2", GameSource.Steam, @"A:\SteamLibrary\steamapps\common\Portal 2", [new(@"A:\SteamLibrary\steamapps\common\Portal 2\portal2.exe", 1_200_000)]),
            new InstalledGame("Hades", GameSource.Epic, @"C:\Epic\Hades", [new(@"C:\Epic\Hades\x64\Hades.exe", 4_500_000), new(@"C:\Epic\Hades\x86\Hades.exe", 4_100_000)]),
            new InstalledGame("The Witcher 3", GameSource.Gog, @"D:\GOG\The Witcher 3", [new(@"D:\GOG\The Witcher 3\bin\x64\witcher3.exe", 52_000_000)]),
            new InstalledGame("Factorio", GameSource.Folder, @"A:\Jeux\Factorio", [new(@"A:\Jeux\Factorio\bin\x64\factorio.exe", 31_000_000)]),
        ], path => path.Contains("Portal 2", StringComparison.Ordinal), [@"A:\Jeux"]));

        yield return ("7-dialogue-programmes", () => new ProcessPickerDialog(
        [
            new RunningProgram("chrome.exe", "Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 14),
            new RunningProgram("Discord.exe", "Discord", @"C:\Users\Joueur\AppData\Local\Discord\app-1.0\Discord.exe", 6),
            new RunningProgram("OneDrive.exe", "Microsoft OneDrive", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", 1),
        ]));

        yield return ("7-dialogue-jaquette", () => new IgdbSearchDialog("Portal 2",
            _ => Task.FromResult<IReadOnlyList<IgdbGame>>(
            [
                new IgdbGame(1, "Portal 2", 2011, "co1rs4", null),
                new IgdbGame(2, "Portal 2: Sixense Perceptual Pack", 2012, "co2abc", null),
            ]),
            _ => Task.FromResult<string?>(null)));
    }
}
