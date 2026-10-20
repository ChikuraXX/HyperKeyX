using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface IStartupRegistrationService
{
    Task<OperationResult<bool>> IsEnabledAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}
