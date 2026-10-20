using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Common;

public sealed record MacroPackageImportResult(
    MacroDefinition Macro,
    bool ReusedExisting,
    Guid? ExistingMacroId);
