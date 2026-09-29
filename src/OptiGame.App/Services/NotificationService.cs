using H.NotifyIcon;
using H.NotifyIcon.Core;
using OptiGame.Core.Logging;

namespace OptiGame.App.Services;

public interface INotificationService
{
    void Show(string title, string message, bool isWarning = false);
}

/// <summary>
/// Bulles de notification de l'icône de la zone de notification. Chaque notification est aussi écrite dans le
/// journal : Windows peut les masquer (mode Ne pas déranger, notifications désactivées par un outil d'optimisation).
/// </summary>
public sealed class NotificationService(FileLog log) : INotificationService
{
    private TaskbarIcon? _icon;

    public void Attach(TaskbarIcon icon) => _icon = icon;

    public void Show(string title, string message, bool isWarning = false)
    {
        var flat = message.Replace(Environment.NewLine, " | ").Replace("\n", " | ");
        if (isWarning) log.Warn($"Notification : {title} — {flat}");
        else log.Info($"Notification : {title} — {flat}");

        _icon?.Dispatcher.Invoke(() =>
            _icon.ShowNotification(title, message, isWarning ? NotificationIcon.Warning : NotificationIcon.Info));
    }
}
