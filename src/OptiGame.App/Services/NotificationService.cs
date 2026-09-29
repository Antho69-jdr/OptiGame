using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace OptiGame.App.Services;

public interface INotificationService
{
    void Show(string title, string message, bool isWarning = false);
}

/// <summary>Bulles de notification de l'icône de la zone de notification.</summary>
public sealed class NotificationService : INotificationService
{
    private TaskbarIcon? _icon;

    public void Attach(TaskbarIcon icon) => _icon = icon;

    public void Show(string title, string message, bool isWarning = false) =>
        _icon?.Dispatcher.Invoke(() =>
            _icon.ShowNotification(title, message, isWarning ? NotificationIcon.Warning : NotificationIcon.Info));
}
