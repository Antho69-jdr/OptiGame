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
public sealed class MemoryRelief(LibraryViewModel library, FileLog log)
{
    private bool _scheduled;
    private string? _reason;

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
        if (_reason is { } reason) log.Info($"{reason} — mémoire : {MemoryUsage.Now().Describe()}");
        _reason = null;
    }
}
