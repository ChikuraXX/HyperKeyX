using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Domain.Profiles;

/// <summary>
/// 表示一个保存的应用配置文件，包含按键配置集合
/// </summary>
public class AppProfile
{
    public Guid ProfileId { get; set; } = Guid.NewGuid();
    public string TargetProcessName { get; set; } = string.Empty;
    public Dictionary<int, KeyConfig> KeyConfigs { get; set; } = new();
    public Dictionary<MouseButtonKind, MouseButtonConfig> MouseButtonConfigs { get; set; } = new();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(TargetProcessName) ? "Global" : TargetProcessName;

    public AppProfile DeepClone()
    {
        return new AppProfile
        {
            ProfileId = ProfileId,
            TargetProcessName = TargetProcessName ?? string.Empty,
            KeyConfigs = (KeyConfigs ?? new Dictionary<int, KeyConfig>())
                .Where(pair => pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone()),
            MouseButtonConfigs = (MouseButtonConfigs ??
                                  new Dictionary<MouseButtonKind, MouseButtonConfig>())
                .Where(pair => pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone())
        };
    }
}
