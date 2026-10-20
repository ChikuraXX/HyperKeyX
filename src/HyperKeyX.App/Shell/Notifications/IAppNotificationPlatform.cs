using HyperKeyX.Application.Platform;

namespace HyperKeyX.Shell.Notifications;

internal interface IAppNotificationPlatform
{
    bool IsSupported();
    bool IsEnabled();
    void Register(string displayName, Uri iconUri);
    void Show(LocalNotification notification);
    void Unregister();
}
