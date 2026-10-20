using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Application.Abstractions.Persistence;

public interface IConfigurationTransferService
{
    Task<OperationResult<int>> ExportAsync(
        Stream destination,
        AutomationConfigurationDocument document,
        CancellationToken cancellationToken = default);

    Task<OperationResult<ConfigurationImportResult>> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
