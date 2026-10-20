using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Abstractions.Persistence;

public interface IMacroPackageTransferService
{
    Task<OperationResult> ExportAsync(
        Stream destination,
        MacroDefinition macro,
        CancellationToken cancellationToken = default);

    Task<OperationResult<MacroPackageImportResult>> ImportAsync(
        Stream source,
        IReadOnlyCollection<MacroDefinition> existingMacros,
        CancellationToken cancellationToken = default);
}
