using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface ISingleInstanceService : IDisposable
{
    event EventHandler? ActivationRequested;

    bool IsPrimary { get; }

    bool TryAcquire();

    OperationResult SignalPrimary();
}
