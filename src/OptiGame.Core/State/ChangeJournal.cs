using OptiGame.Core.Changes;

namespace OptiGame.Core.State;

/// <summary>
/// Applique des changements de façon réversible, en « write-ahead » : l'état d'origine de chaque réglage est
/// persisté sur disque AVANT la moindre écriture. Si l'appli plante à n'importe quel moment, une nouvelle instance
/// chargée sur le même fichier peut tout remettre en l'état.
/// </summary>
public sealed class ChangeJournal
{
    private readonly IStateStore<JournalDocument> _store;
    private readonly SettingAccessors _accessors;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private JournalDocument _document;

    public ChangeJournal(IStateStore<JournalDocument> store, SettingAccessors accessors, TimeProvider? time = null)
    {
        _store = store;
        _accessors = accessors;
        _time = time ?? TimeProvider.System;
        _document = store.Load() ?? NewDocument(context: null);
    }

    /// <summary>Vrai s'il reste des réglages à restaurer (ex. session interrompue par un crash).</summary>
    public bool HasPendingEntries
    {
        get { lock (_lock) return _document.Entries.Count > 0; }
    }

    public string? Context
    {
        get { lock (_lock) return _document.Context; }
    }

    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_lock) return _document.Entries.Select(e => e.Clone()).ToList(); }
    }

    public bool IsActive(string changeId)
    {
        lock (_lock) return _document.Entries.Any(e => e.ChangeIds.Contains(changeId));
    }

    /// <summary>Changements encore actifs (au moins un réglage non restauré), dans l'ordre d'application.</summary>
    public IReadOnlyList<ChangeRecord> ActiveChanges
    {
        get { lock (_lock) return [.. _document.Changes]; }
    }

    /// <summary>Renseigne le contexte (ex. nom du jeu) quand le journal est vide.</summary>
    public void SetContext(string? context)
    {
        lock (_lock)
        {
            _document.Context = context;
            if (_document.Entries.Count > 0)
            {
                _store.Save(_document);
            }
        }
    }

    /// <summary>
    /// Capture l'état d'origine, le persiste, puis applique les écritures. En cas d'échec d'une écriture,
    /// les écritures déjà faites sont annulées et l'exception est propagée.
    /// </summary>
    public void Apply(ReversibleChange change)
    {
        lock (_lock)
        {
            var before = _document.Clone();
            List<(SettingTarget Target, SettingValue Previous)> previousValues;

            // 1 et 2. Capture (lecture seule) puis persistance AVANT toute écriture.
            // Une erreur ici n'a rien modifié sur le système.
            try
            {
                previousValues = Capture(change);
                _document.Changes.RemoveAll(c => c.Id == change.Id);
                _document.Changes.Add(new ChangeRecord(change.Id, change.Title, change.What, change.RequiresReboot, _time.GetUtcNow()));
                if (before.Entries.Count == 0)
                {
                    _document.CreatedAt = _time.GetUtcNow();
                }
                _store.Save(_document);
            }
            catch
            {
                _document = before;
                throw;
            }

            // 3. Application. Une écriture qui lève une exception est considérée comme non effectuée :
            // seules les écritures réussies sont annulées.
            var succeeded = 0;
            try
            {
                foreach (var write in change.Writes)
                {
                    _accessors.Write(write.Target, write.NewValue);
                    succeeded++;
                }
            }
            catch
            {
                RollBack(previousValues.Take(succeeded), before);
                throw;
            }
        }
    }

    private List<(SettingTarget Target, SettingValue Previous)> Capture(ReversibleChange change)
    {
        var previousValues = new List<(SettingTarget Target, SettingValue Previous)>();
        foreach (var write in change.Writes)
        {
            var entry = Find(write.Target);
            if (entry is null)
            {
                var original = _accessors.Read(write.Target);
                _document.Entries.Add(new JournalEntry
                {
                    Target = write.Target,
                    Original = original,
                    Applied = write.NewValue,
                    ChangeIds = [change.Id],
                    CapturedAt = _time.GetUtcNow(),
                });
                previousValues.Add((write.Target, original));
            }
            else
            {
                // Déjà modifié par l'appli : on garde l'origine la plus ancienne.
                previousValues.Add((write.Target, entry.Applied));
                entry.Applied = write.NewValue;
                if (!entry.ChangeIds.Contains(change.Id))
                {
                    entry.ChangeIds.Add(change.Id);
                }
            }
        }
        return previousValues;
    }

    /// <summary>Annule un seul changement (ex. une correction du diagnostic).</summary>
    public RestoreReport Undo(string changeId)
    {
        lock (_lock)
        {
            var report = new RestoreReport();
            for (var i = _document.Entries.Count - 1; i >= 0; i--)
            {
                var entry = _document.Entries[i];
                if (!entry.ChangeIds.Contains(changeId))
                {
                    continue;
                }

                if (entry.ChangeIds.Count > 1)
                {
                    // Un autre changement actif dépend encore de ce réglage : il sera restauré avec lui.
                    entry.ChangeIds.Remove(changeId);
                    report.StillHeld.Add(entry.Target);
                    continue;
                }

                // En cas d'échec, l'entrée garde son identifiant : le changement reste « actif ».
                RestoreEntry(entry, report);
            }

            PersistOrDelete();
            return report;
        }
    }

    /// <summary>Restaure tout, en ordre inverse. Les échecs n'empêchent pas les restaurations suivantes.</summary>
    public RestoreReport RestoreAll()
    {
        lock (_lock)
        {
            var report = new RestoreReport();
            for (var i = _document.Entries.Count - 1; i >= 0; i--)
            {
                RestoreEntry(_document.Entries[i], report);
                // Persister après chaque réglage : un crash pendant la restauration ne rejoue que le reste.
                PersistOrDelete();
            }

            PersistOrDelete();
            return report;
        }
    }

    private void RestoreEntry(JournalEntry entry, RestoreReport report)
    {
        try
        {
            var current = _accessors.Read(entry.Target);
            if (!current.Equals(entry.Applied) && !current.Equals(entry.Original))
            {
                report.ModifiedExternally.Add(entry.Target);
            }

            _accessors.Write(entry.Target, entry.Original);
            _document.Entries.Remove(entry);
            report.Restored.Add(entry.Target);
        }
        catch (Exception ex)
        {
            // L'entrée reste dans le journal pour une nouvelle tentative.
            report.Failed.Add(new RestoreFailure(entry.Target, ex.Message));
        }
    }

    private void RollBack(IEnumerable<(SettingTarget Target, SettingValue Previous)> attempted, JournalDocument before)
    {
        var rollbackFailed = false;
        foreach (var (target, previous) in attempted.Reverse())
        {
            try
            {
                _accessors.Write(target, previous);
            }
            catch
            {
                rollbackFailed = true;
            }
        }

        if (!rollbackFailed)
        {
            _document = before;
            PersistOrDelete();
        }
        // Sinon on garde le journal tel quel : il contient les valeurs d'origine, RestoreAll pourra réessayer.
    }

    private void PersistOrDelete()
    {
        // Un changement dont plus aucun réglage n'est modifié n'est plus actif.
        _document.Changes.RemoveAll(c => !_document.Entries.Any(e => e.ChangeIds.Contains(c.Id)));

        if (_document.Entries.Count == 0)
        {
            _store.Delete();
            _document = NewDocument(_document.Context);
        }
        else
        {
            _store.Save(_document);
        }
    }

    private JournalEntry? Find(SettingTarget target) => _document.Entries.FirstOrDefault(e => e.Target == target);

    private JournalDocument NewDocument(string? context) => new() { CreatedAt = _time.GetUtcNow(), Context = context };
}

public sealed record RestoreFailure(SettingTarget Target, string Error);

public sealed class RestoreReport
{
    public List<SettingTarget> Restored { get; } = [];

    public List<RestoreFailure> Failed { get; } = [];

    /// <summary>Réglages modifiés par autre chose que l'appli depuis l'application (restaurés quand même).</summary>
    public List<SettingTarget> ModifiedExternally { get; } = [];

    /// <summary>Réglages non restaurés car encore utilisés par un autre changement actif.</summary>
    public List<SettingTarget> StillHeld { get; } = [];

    public bool Success => Failed.Count == 0;
}
