using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface ILocalNotificationService : IDisposable
{
    bool IsInitialized { get; }

    OperationResult Initialize();

    OperationResult Show(LocalNotification notification);
}
