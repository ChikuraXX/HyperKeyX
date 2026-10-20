using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Domain.Configuration;

/// <summary>
/// 将旧触发器内嵌步骤迁移为应用级宏定义和稳定引用。
/// </summary>
public static class LegacyConfigurationMigrator
{
    public static AutomationConfigurationDocument Migrate(
        IEnumerable<AppProfile?> legacyProfiles)
    {
        ArgumentNullException.ThrowIfNull(legacyProfiles);

        var profiles = ProfileConfigurationNormalizer.NormalizeForStorage(legacyProfiles);
        var macros = new List<MacroDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            foreach (var pair in profile.KeyConfigs.OrderBy(pair => pair.Key))
            {
                MovePayload(
                    profile,
                    $"Key 0x{pair.Key:X2}",
                    pair.Value,
                    macros,
                    names);
            }

            foreach (var pair in profile.MouseButtonConfigs.OrderBy(pair => pair.Key))
            {
                MovePayload(
                    profile,
                    $"Mouse {pair.Key}",
                    pair.Value,
                    macros,
                    names);
            }
        }

        return new AutomationConfigurationDocument
        {
            Macros = macros,
            Profiles = ProfileConfigurationNormalizer
                .NormalizeForDocumentStorage(profiles)
        };
    }

    private static void MovePayload(
        AppProfile profile,
        string triggerName,
        TriggerBehaviorConfig trigger,
        ICollection<MacroDefinition> macros,
        ISet<string> names)
    {
        if (trigger.MacroSteps.Count == 0)
        {
            trigger.MacroId = null;
            trigger.MacroSteps = [];
            return;
        }

        var macroId = Guid.NewGuid();
        var macro = new MacroDefinition
        {
            MacroId = macroId,
            Name = AllocateName(
                $"{profile.DisplayName} - {triggerName} - Migrated macro",
                names),
            Steps = trigger.MacroSteps
        }.DeepClone();

        // 每个旧触发器独立生成定义，避免相同内容在迁移后产生意外联动。
        macros.Add(macro);
        trigger.MacroId = macroId;
        trigger.MacroSteps = [];
    }

    private static string AllocateName(string requestedName, ISet<string> names)
    {
        var root = requestedName.Trim();
        if (root.Length > AutomationConfigurationValidator.MaximumNameLength)
            root = root[..AutomationConfigurationValidator.MaximumNameLength];

        if (names.Add(root))
            return root;

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" ({suffix})";
            var rootLength = AutomationConfigurationValidator.MaximumNameLength -
                             suffixText.Length;
            var candidate = $"{root[..Math.Min(root.Length, rootLength)]}{suffixText}";
            if (names.Add(candidate))
                return candidate;
        }
    }
}
