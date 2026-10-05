namespace OptiGame.App.Services;

/// <summary>
/// Garde des modifications non enregistrées (fiche du jeu), pour les sorties qui ne passent pas par la page : « Quitter » de
/// la zone de notification. La bibliothèque s'y inscrit quand elle existe ; sinon rien ne peut être en attente.
/// </summary>
public sealed class UnsavedChangesGuard
{
    private Func<bool>? _confirmDiscard;

    public void Register(Func<bool> confirmDiscard) => _confirmDiscard = confirmDiscard;

    /// <summary>Vrai s'il n'y a rien en attente, ou si l'utilisateur accepte de perdre ses modifications.</summary>
    public bool ConfirmDiscard() => _confirmDiscard?.Invoke() ?? true;
}
