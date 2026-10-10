using System.Runtime;
using System.Windows;
using System.Windows.Threading;
using OptiGame.App.ViewModels;
using OptiGame.Core.Logging;

namespace OptiGame.App.Services;

/// <summary>
/// Libère ce qui peut l'être (jaquettes des jeux non installés au-delà de la première page, cache d'images), puis UN passage
/// complet du ramasse-miettes une fois l'interface au repos : sans nouvelle allocation (appli au repos pendant une partie),
/// rien d'autre ne le déclencherait, et les images décodées (mémoire hors .NET) ne sont rendues qu'à ce moment. Les demandes
/// rapprochées (fenêtre fermée puis début de partie) ne font qu'un passage. Ce n'est pas un « vidage de RAM » : seule la
/// mémoire qu'OptiGame n'utilise plus est rendue, une fois.
/// </summary>
public sealed class MemoryRelief(LibraryViewModel library, FileLog log, GameTimeGate gate)
{
    private bool _scheduled;
    private string? _reason;

    /// <summary>
    /// Filet de sécurité, fenêtre ouverte : de la mémoire HORS .NET (ressources de rendu, images) peut s'accumuler alors que le tas
    /// .NET reste minuscule, et rien ne déclenche alors le ramasse-miettes qui la rendrait. Relevé le 2026-10-10 sur la copie
    /// installée (1.16.2, fenêtre ouverte, session à distance) : 604 Mo privés, 251 après un passage complet ; +230 Mo en 15 min
    /// par paquets. Toutes les 5 minutes : si la mémoire privée a grandi de 150 Mo depuis le dernier passage, un passage complet
    /// (moins de 100 ms), jamais pendant une partie (le passage du début de partie et la fin s'en chargent). Chaque passage est
    /// écrit au journal, pour retrouver la cause.
    /// </summary>
    private const long GrowthBeforeCollect = 150L * 1024 * 1024;

    private static readonly TimeSpan WatchInterval = TimeSpan.FromMinutes(5);
    private long _baseline;
    private DispatcherTimer? _watch;

    public void StartWatching()
    {
        _baseline = MemoryUsage.Now().PrivateBytes;
        _watch = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchInterval };
        _watch.Tick += (_, _) => CheckGrowth();
        _watch.Start();
    }

    private void CheckGrowth()
    {
        if (gate.InGame) return;
        var before = MemoryUsage.Now().PrivateBytes;
        if (before - _baseline < GrowthBeforeCollect) return;
        var grown = before - _baseline;
        Collect();
        log.Info($"Mémoire : nettoyage automatique, privée {before / 1048576} → {_baseline / 1048576} Mo " +
                 $"(+{grown / 1048576} Mo depuis le précédent passage) — {MemoryUsage.Now().Describe()}");
    }

    /// <param name="logReason">Si donné, la mémoire restante est écrite dans le journal après le nettoyage.</param>
    public void Release(string? logReason = null)
    {
        library.ReleaseCovers();
        if (logReason is not null) _reason = logReason;
        if (_scheduled) return;
        _scheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(Collect, DispatcherPriority.ContextIdle);
    }

    private void Collect()
    {
        _scheduled = false;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers(); // images WPF : leurs pixels sont rendus par les finaliseurs
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        _baseline = MemoryUsage.Now().PrivateBytes;
        if (_reason is { } reason) log.Info($"{reason} — mémoire : {MemoryUsage.Now().Describe()}");
        _reason = null;
    }
}
