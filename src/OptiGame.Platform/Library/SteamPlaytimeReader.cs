using OptiGame.Core.Logging;
using OptiGame.Core.Playtime;
using OptiGame.Core.Profiles;

namespace OptiGame.Platform.Library;

/// <summary>
/// Temps de jeu Steam des profils, lu dans localconfig.vdf du compte Steam courant (lecture seule). Steam réécrit ce
/// fichier à la fin d'une partie : un FileSystemWatcher le signale (événement de Windows, aucune scrutation).
/// </summary>
public sealed class SteamPlaytimeReader(FileLog log) : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly Lock _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>Steam a réécrit localconfig.vdf (déclenché une fois, 2 s après la dernière écriture).</summary>
    public event EventHandler? Changed;

    /// <summary>Temps Steam par profil (jeux Steam uniquement). Lit des fichiers : à appeler hors du thread UI.</summary>
    public IReadOnlyDictionary<Guid, SteamPlaytimeEntry> Read(IEnumerable<GameProfile> profiles)
    {
        var result = new Dictionary<Guid, SteamPlaytimeEntry>();
        if (LocalConfigPath() is not { } path) return result;
        try
        {
            string text;
            // Steam peut écrire le fichier au même moment : lecture sans le bloquer.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            var byApp = SteamPlaytime.Parse(text);
            var apps = GameLibraryScanner.SteamApps();
            foreach (var profile in profiles)
            {
                if (GameLibraryScanner.SteamAppIdFor(profile, apps) is { } appId && byApp.TryGetValue(appId, out var entry))
                {
                    result[profile.Id] = entry;
                }
            }
            Watch(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            log.Error($"Temps de jeu Steam illisible ({path})", ex);
        }
        return result;
    }

    /// <summary>
    /// localconfig.vdf du compte à lire : compte connecté si Steam tourne (ActiveProcess\ActiveUser), sinon dernière
    /// connexion de loginusers.vdf, sinon le localconfig.vdf modifié le plus récemment.
    /// </summary>
    public static string? LocalConfigPath()
    {
        if (GameLibraryScanner.SteamPath() is not { } steam) return null;
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return null;

        string? For(string? accountId) =>
            accountId is { Length: > 0 } && Path.Combine(userdata, accountId, "config", "localconfig.vdf") is var file && File.Exists(file) ? file : null;

        using (var active = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
        {
            if (active?.GetValue("ActiveUser") is int user && user > 0 && For(user.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } running)
            {
                return running;
            }
        }

        var loginUsers = Path.Combine(steam, "config", "loginusers.vdf");
        try
        {
            if (File.Exists(loginUsers) && For(SteamPlaytime.MostRecentAccountId(File.ReadAllText(loginUsers))) is { } recent)
            {
                return recent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Pas bloquant : on retombe sur le fichier le plus récent.
        }

        return Directory.EnumerateDirectories(userdata)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void Watch(string path)
    {
        lock (_lock)
        {
            if (_watcher is not null && string.Equals(Path.Combine(_watcher.Path, _watcher.Filter), path, StringComparison.OrdinalIgnoreCase)) return;
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            // Steam peut écrire en place ou remplacer le fichier : les trois cas déclenchent une relecture.
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Created += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void Schedule()
    {
        lock (_lock)
        {
            _debounce ??= new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
