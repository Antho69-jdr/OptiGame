using System.Runtime.InteropServices;
using OptiGame.Core.Drivers;
using OptiGame.Core.Logging;

namespace OptiGame.Platform.Drivers;

/// <summary>
/// Pilotes proposés par Windows Update, via l'API Windows Update Agent (COM « Microsoft.Update.Session »). Lecture seule :
/// une recherche ne télécharge ni n'installe rien. Vérifié sous AtlasOS le 2026-09-30 : fonctionne (service wuauserv
/// en démarrage manuel, lancé à la demande par Windows), ≈ 25 s.
/// </summary>
public sealed class WindowsUpdateDriverSearch(FileLog log)
{
    private const string Criteria = "IsInstalled=0 and Type='Driver' and IsHidden=0";

    /// <summary>Recherche (bloquante, longue) : à appeler hors du thread UI.</summary>
    public IReadOnlyList<WindowsUpdateDriver> Search()
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
                          ?? throw new InvalidOperationException("Windows Update Agent est absent de ce Windows.");
        dynamic session = Activator.CreateInstance(sessionType)!;
        try
        {
            session.ClientApplicationID = "OptiGame";
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search(Criteria);
            var drivers = new List<WindowsUpdateDriver>();
            foreach (dynamic update in result.Updates)
            {
                drivers.Add(new WindowsUpdateDriver(
                    UpdateId: (string)update.Identity.UpdateID,
                    Title: (string)update.Title,
                    DriverClass: Try(() => (string?)update.DriverClass),
                    DriverDate: Try(() => (DateTime?)update.DriverVerDate),
                    Manufacturer: Try(() => (string?)update.DriverManufacturer),
                    Model: Try(() => (string?)update.DriverModel),
                    // RebootBehavior : 0 = jamais, 1 = toujours, 2 = peut le demander.
                    MayRequireReboot: Try(() => (int?)update.InstallationBehavior.RebootBehavior) is > 0,
                    SizeBytes: Try(() => (long?)Convert.ToInt64(update.MaxDownloadSize))));
            }
            log.Info($"Windows Update : {drivers.Count} pilote(s) proposé(s) — " + string.Join(" ; ", drivers.Select(d => d.Title)));
            return drivers;
        }
        finally
        {
            Marshal.FinalReleaseComObject(session);
        }
    }

    /// <summary>Propriété d'un pilote absente ou illisible : ignorée plutôt que de faire échouer toute la recherche.</summary>
    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            return default;
        }
    }
}
