using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Application.Common;

public sealed record ConfigurationImportResult(
    AutomationConfigurationDocument Document,
    bool WasMigrated,
    bool RequiresExplicitSave);
