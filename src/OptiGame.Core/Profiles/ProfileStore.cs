using OptiGame.Core.State;

namespace OptiGame.Core.Profiles;

/// <summary>Profils de jeu, persistés dans <c>profiles.json</c>. Renvoie toujours des copies.</summary>
public sealed class ProfileStore
{
    private readonly IStateStore<ProfilesDocument> _store;
    private readonly Lock _lock = new();
    private ProfilesDocument _document;

    public ProfileStore(IStateStore<ProfilesDocument> store)
    {
        _store = store;
        _document = store.Load() ?? new ProfilesDocument();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<GameProfile> GetAll()
    {
        lock (_lock) return _document.Profiles.Select(p => p.Clone()).ToList();
    }

    public GameProfile? Find(Guid id)
    {
        lock (_lock) return _document.Profiles.FirstOrDefault(p => p.Id == id)?.Clone();
    }

    /// <summary>Profil actif correspondant à l'exe, ou null.</summary>
    public GameProfile? FindEnabledFor(string exePath)
    {
        lock (_lock) return _document.Profiles.FirstOrDefault(p => p.Enabled && p.Matches(exePath))?.Clone();
    }

    /// <summary>Ajoute ou remplace le profil. Lève <see cref="ProfileValidationException"/> s'il est invalide.</summary>
    public void Save(GameProfile profile)
    {
        var errors = ProfileValidator.Validate(profile, GetAll());
        if (errors.Count > 0)
        {
            throw new ProfileValidationException(errors);
        }

        lock (_lock)
        {
            var copy = profile.Clone();
            var index = _document.Profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                // Les jaquettes ne changent que par SetArtwork : un éditeur ouvert avant qu'une jaquette soit trouvée
                // ne doit pas l'effacer en enregistrant.
                var stored = _document.Profiles[index];
                copy.IgdbGameId = stored.IgdbGameId;
                copy.CoverImageId = stored.CoverImageId;
                copy.HeroImageId = stored.HeroImageId;
                copy.DockOrder = stored.DockOrder; // idem pour le dock : ne change que par SetPinned / MoveInDock
                _document.Profiles[index] = copy;
            }
            else
            {
                _document.Profiles.Add(copy);
            }
            _store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Associe (ou retire, avec des null) le jeu IGDB et ses images. Sans effet si le profil n'existe plus.</summary>
    public void SetArtwork(Guid id, long? igdbGameId, string? coverImageId, string? heroImageId)
    {
        lock (_lock)
        {
            if (_document.Profiles.FirstOrDefault(p => p.Id == id) is not { } profile) return;
            profile.IgdbGameId = igdbGameId;
            profile.CoverImageId = coverImageId;
            profile.HeroImageId = heroImageId;
            _store.Save(_document);
        }
        ArtworkChanged?.Invoke(this, id);
    }

    /// <summary>Jaquette modifiée (distinct de <see cref="Changed"/> : n'influe pas sur la détection des jeux).</summary>
    public event EventHandler<Guid>? ArtworkChanged;

    /// <summary>Contenu ou ordre du dock modifié.</summary>
    public event EventHandler? DockChanged;

    /// <summary>Jeux épinglés au dock, dans l'ordre.</summary>
    public IReadOnlyList<GameProfile> GetDock()
    {
        lock (_lock) return DockedLocked().Select(p => p.Clone()).ToList();
    }

    /// <summary>Épingle (à la fin du dock) ou retire un jeu.</summary>
    public void SetPinned(Guid id, bool pinned)
    {
        lock (_lock)
        {
            if (_document.Profiles.FirstOrDefault(p => p.Id == id) is not { } profile) return;
            if (pinned == profile.DockOrder.HasValue) return;
            profile.DockOrder = pinned ? int.MaxValue : null;
            RenumberDockLocked();
            _store.Save(_document);
        }
        DockChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Déplace un jeu épinglé à la position donnée (0 = premier).</summary>
    public void MoveInDock(Guid id, int newIndex)
    {
        lock (_lock)
        {
            var docked = DockedLocked();
            if (docked.FirstOrDefault(p => p.Id == id) is not { } profile) return;
            docked.Remove(profile);
            docked.Insert(Math.Clamp(newIndex, 0, docked.Count), profile);
            for (var i = 0; i < docked.Count; i++) docked[i].DockOrder = i;
            _store.Save(_document);
        }
        DockChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<GameProfile> DockedLocked() =>
        _document.Profiles.Where(p => p.DockOrder.HasValue).OrderBy(p => p.DockOrder).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    private void RenumberDockLocked()
    {
        var docked = DockedLocked();
        for (var i = 0; i < docked.Count; i++) docked[i].DockOrder = i;
    }

    public void Remove(Guid id)
    {
        bool wasDocked;
        lock (_lock)
        {
            wasDocked = _document.Profiles.Any(p => p.Id == id && p.DockOrder.HasValue);
            if (_document.Profiles.RemoveAll(p => p.Id == id) == 0) return;
            RenumberDockLocked();
            _store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (wasDocked) DockChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ProfileValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
