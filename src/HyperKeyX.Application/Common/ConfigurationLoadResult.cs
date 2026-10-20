using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Application.Common;

public sealed record ConfigurationLoadResult(
    AutomationConfigurationDocument Document,
    ConfigurationLoadSource Source,
    bool WasMigrated,
    bool IsSaveBlocked,
    string? WarningMessage)
{
    public bool UsedBackup => Source == ConfigurationLoadSource.Backup;
}
