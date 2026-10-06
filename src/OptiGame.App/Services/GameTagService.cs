using System.Net.Http;
using System.Windows;
using OptiGame.Core.Artwork;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Platform.Artwork;
using OptiGame.Platform.Library;

namespace OptiGame.App.Services;

/// <summary>
/// Genres et types de TOUS les jeux de Mes jeux (Steam, Epic, GOG, ajoutés à la main), complétés par IGDB : sans eux, choisir un
/// genre faisait disparaître tous les jeux hors Steam. Seulement si les identifiants IGDB sont renseignés ; en tâche de fond, par
/// paquets de 10 noms (requête multiple), jamais pendant une partie ; résultats gardés dans taxonomy.json (une seule recherche
/// par jeu). Seuls les noms des jeux sont envoyés.
/// </summary>
public sealed class GameTagService(IgdbClient igdb, GameTagCache cache, GameTimeGate gate, FileLog log)
{
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _queued = [];
    private bool _running;

    /// <summary>Des genres viennent d'arriver (thread UI) : Mes jeux les applique et reconstruit ses filtres.</summary>
    public event EventHandler? Updated;

    public bool IsAvailable => igdb.IsConfigured;

    /// <summary>Recherches en cours ou en attente (captures de développement : attendre la fin).</summary>
    public bool IsBusy => _running || _queue.Count > 0;

    public GameTags? Find(string name) => cache.Find(name);

    /// <summary>Demande les genres des jeux encore inconnus (thread UI). Sans effet sans identifiants IGDB.</summary>
    public void Request(IEnumerable<string> names)
    {
        if (!igdb.IsConfigured) return;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || !_queued.Add(GameTagCache.Key(name)) || !cache.NeedsLookup(name)) continue;
            _queue.Enqueue(name);
        }
        if (_queue.Count > 0 && !_running) gate.RunOrDefer("game-tags", () => _ = RunAsync());
    }

    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        var found = 0;
        var total = 0;
        try
        {
            while (_queue.Count > 0)
            {
                if (gate.InGame)
                {
                    gate.RunOrDefer("game-tags", () => _ = RunAsync()); // reprise à la fin de la partie
                    return;
                }
                var batch = new List<string>();
                while (batch.Count < Igdb.MaxQueriesPerMultiQuery && _queue.Count > 0) batch.Add(_queue.Dequeue());
                var tags = await igdb.TaxonomyAsync(batch);
                await Task.Run(() => cache.Store(batch.Select((name, i) => (name, tags[i])).ToList()));
                found += tags.Count(t => t is not null);
                total += batch.Count;
                // Toutes les 5 requêtes et à la fin : la grille n'est pas reconstruite à chaque paquet.
                if (_queue.Count == 0 || total % (Igdb.MaxQueriesPerMultiQuery * 5) == 0) Updated?.Invoke(this, EventArgs.Empty);
            }
            if (total > 0) log.Info($"Genres IGDB : {found} jeux sur {total} trouvés.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException or IOException)
        {
            // Hors ligne, identifiants refusés… : les jeux restants seront redemandés au prochain chargement de Mes jeux.
            log.Warn($"Genres IGDB interrompus après {total} jeux : {ex.Message}");
            _queue.Clear();
            _queued.Clear();
            if (total > 0) Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _running = false;
        }
    }
}
