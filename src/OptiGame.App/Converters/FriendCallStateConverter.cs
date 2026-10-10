using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OptiGame.App.Call;

namespace OptiGame.App.Converters;

/// <summary>
/// Ligne d'un ami en ligne pendant un appel (MultiBinding : l'ami, CallViewModel.FriendCallStates, CallViewModel.CanInvite).
/// Paramètre « text » : « dans l'appel » / « ça sonne… » / rien ; « invite » : bouton « Inviter » visible seulement si l'ami n'est
/// ni dans l'appel ni en train de sonner, et qu'il reste de la place.
/// </summary>
public sealed class FriendCallStateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var state = values.Length > 1 && values[0] is CallFriend friend && values[1] is IReadOnlyDictionary<string, string> states
            && states.TryGetValue(friend.DisplayName, out var found) ? found : "";
        if (parameter as string == "invite")
        {
            return state.Length == 0 && values.Length > 2 && values[2] is true ? Visibility.Visible : Visibility.Collapsed;
        }
        return state;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
