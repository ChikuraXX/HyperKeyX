using HyperKeyX.Application.Platform;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace HyperKeyX.Shell.Notifications;

internal sealed class AppNotificationPlatform : IAppNotificationPlatform
{
    private readonly AppNotificationManager _manager = AppNotificationManager.Default;

    public bool IsSupported() => AppNotificationManager.IsSupported();

    public bool IsEnabled() =>
        _manager.Setting == AppNotificationSetting.Enabled;

    public void Register(string displayName, Uri iconUri) =>
        _manager.Register(displayName, iconUri);

    public void Show(LocalNotification notification)
    {
        var value = new AppNotificationBuilder()
            .AddText(notification.Title)
            .AddText(notification.Message)
            .SetTag(notification.Tag)
            .SetGroup(notification.Group)
            .BuildNotification();
        _manager.Show(value);
    }

    public void Unregister() => _manager.Unregister();
}
