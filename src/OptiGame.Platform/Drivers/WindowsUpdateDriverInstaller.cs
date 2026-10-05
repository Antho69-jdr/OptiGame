using System.Runtime.InteropServices;
using OptiGame.Core.Drivers;
using OptiGame.Core.Text;

namespace OptiGame.Platform.Drivers;

/// <summary>
/// Téléchargement et installation de pilotes choisis par l'utilisateur, par Windows Update lui-même (API Windows Update
/// Agent). Appelé uniquement via IPrivilegedOperations : l'installation exige les droits administrateur.
/// </summary>
internal static class WindowsUpdateDriverInstaller
{
    public static WindowsUpdateInstallReport Install(IReadOnlyList<string> updateIds, Action<string> progress)
    {
        // Identifiants vérifiés : ils entrent dans une requête Windows Update.
        var ids = updateIds.Select(id => Guid.TryParse(id, out var guid) ? guid.ToString("D") : throw new ArgumentException($"Identifiant invalide : {id}"))
            .Distinct().ToList();
        if (ids.Count == 0) return new WindowsUpdateInstallReport([], false, []);

        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new InvalidOperationException("Windows Update Agent est absent.");
        var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl") ?? throw new InvalidOperationException("Windows Update Agent est absent.");
        dynamic session = Activator.CreateInstance(sessionType)!;
        try
        {
            session.ClientApplicationID = "OptiGame";
            progress("Recherche des pilotes choisis dans Windows Update…");
            dynamic found = session.CreateUpdateSearcher().Search(
                string.Join(" or ", ids.Select(id => $"IsInstalled=0 and Type='Driver' and UpdateID='{id}'")));

            dynamic updates = Activator.CreateInstance(collectionType)!;
            var titles = new List<string>();
            var skipped = new List<string>();
            foreach (dynamic update in found.Updates)
            {
                if (!(bool)update.EulaAccepted)
                {
                    // Jamais accepté à la place de l'utilisateur.
                    skipped.Add($"{(string)update.Title} : licence à accepter depuis Windows Update");
                    continue;
                }
                updates.Add(update);
                titles.Add((string)update.Title);
            }
            if (titles.Count == 0) return new WindowsUpdateInstallReport([], false, skipped);

            progress($"Téléchargement par Windows Update ({FrenchText.Count(titles.Count, "pilote", "pilotes")})…");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = updates;
            downloader.Download();

            progress("Installation par Windows Update…");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = updates;
            dynamic result = installer.Install();

            var results = titles.Select((title, i) => new WindowsUpdateInstallResult(title, (int)result.GetUpdateResult(i).ResultCode)).ToList();
            return new WindowsUpdateInstallReport(results, (bool)result.RebootRequired, skipped);
        }
        finally
        {
            Marshal.FinalReleaseComObject(session);
        }
    }
}
