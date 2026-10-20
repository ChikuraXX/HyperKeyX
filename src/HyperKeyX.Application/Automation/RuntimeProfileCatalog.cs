using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 保存与编辑状态隔离的运行时配置快照，并统一选择专属配置或 Global。
/// </summary>
public sealed class RuntimeProfileCatalog : IRuntimeProfileCatalog
{
    private readonly object _sync = new();
    private List<AppProfile> _profiles = [];

    public event EventHandler? ProfilesReplaced;

    public void ReplaceProfiles(IReadOnlyCollection<AppProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var snapshots = ProfileConfigurationNormalizer
            .NormalizeForStorage(profiles)
            .Select(profile => profile.DeepClone())
            .ToList();

        lock (_sync)
            _profiles = snapshots;

        ProfilesReplaced?.Invoke(this, EventArgs.Empty);
    }

    public AppProfile? ResolveKnownProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        var normalized = Path.GetFileName(processName.Trim());
        lock (_sync)
        {
            var profile = _profiles.FirstOrDefault(candidate =>
                              candidate.TargetProcessName.Length > 0 &&
                              string.Equals(
                                  candidate.TargetProcessName,
                                  normalized,
                                  StringComparison.OrdinalIgnoreCase))
                          ?? _profiles.FirstOrDefault(candidate =>
                              candidate.TargetProcessName.Length == 0);
            return profile?.DeepClone();
        }
    }
}
