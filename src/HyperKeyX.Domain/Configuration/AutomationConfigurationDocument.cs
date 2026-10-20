using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Domain.Configuration;

/// <summary>
/// 宏定义与触发器引用共同持久化的版本化根文档。
/// </summary>
public sealed class AutomationConfigurationDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<MacroDefinition> Macros { get; set; } = [];
    public List<AppProfile> Profiles { get; set; } = [];

    public AutomationConfigurationDocument DeepClone() => new()
    {
        SchemaVersion = SchemaVersion,
        Macros = (Macros ?? [])
            .Where(macro => macro is not null)
            .Select(macro => macro.DeepClone())
            .ToList(),
        Profiles = (Profiles ?? [])
            .Where(profile => profile is not null)
            .Select(profile => profile.DeepClone())
            .ToList()
    };
}
