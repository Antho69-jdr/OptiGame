using OptiGame.Core.State;

namespace OptiGame.Core.Changes;

/// <summary>
/// Une modification proposée à l'utilisateur. Elle est décrite par des données (<see cref="Writes"/>) et non
/// par du code, pour que le journal puisse la restaurer même après un crash de l'appli.
/// </summary>
public sealed record ReversibleChange
{
    /// <summary>Identifiant stable, ex. <c>fix.game-dvr.background-recording</c>.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Ce qui change concrètement (affiché avant confirmation).</summary>
    public required string What { get; init; }

    /// <summary>Pourquoi ce changement (affiché avant confirmation).</summary>
    public required string Why { get; init; }

    public bool RequiresAdmin { get; init; }

    public bool RequiresReboot { get; init; }

    /// <summary>Avertissement supplémentaire (compromis sécurité, dépendance matérielle…).</summary>
    public string? Warning { get; init; }

    public required IReadOnlyList<SettingWrite> Writes { get; init; }
}
