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
            var index = _document.Profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) _document.Profiles[index] = profile.Clone();
            else _document.Profiles.Add(profile.Clone());
            _store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        lock (_lock)
        {
            if (_document.Profiles.RemoveAll(p => p.Id == id) == 0) return;
            _store.Save(_document);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ProfileValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
