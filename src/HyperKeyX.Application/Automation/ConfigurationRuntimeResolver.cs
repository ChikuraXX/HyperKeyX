using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 将引用型配置文档解析为现有自动化运行时可直接消费的快照。
/// </summary>
public static class ConfigurationRuntimeResolver
{
    public static OperationResult<IReadOnlyList<AppProfile>> Resolve(
        AutomationConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var validation = AutomationConfigurationValidator.Validate(document);
        if (!validation.IsValid)
        {
            return OperationResult<IReadOnlyList<AppProfile>>.Failure(
                "The automation configuration is invalid.");
        }

        var macros = (document.Macros ?? [])
            .ToDictionary(macro => macro.MacroId);
        var profiles = (document.Profiles ?? [])
            .Select(profile => profile.DeepClone())
            .ToList();

        foreach (var profile in profiles)
        {
            foreach (var trigger in profile.KeyConfigs.Values)
                MaterializeTrigger(trigger, macros);
            foreach (var trigger in profile.MouseButtonConfigs.Values)
                MaterializeTrigger(trigger, macros);
        }

        return OperationResult<IReadOnlyList<AppProfile>>.Success(
            ProfileConfigurationNormalizer.NormalizeForStorage(profiles));
    }

    private static void MaterializeTrigger(
        TriggerBehaviorConfig trigger,
        IReadOnlyDictionary<Guid, MacroDefinition> macros)
    {
        if (!trigger.IsMacroEnabled ||
            trigger.MacroId is not Guid macroId ||
            !macros.TryGetValue(macroId, out var macro))
        {
            trigger.MacroSteps = [];
            trigger.RuntimeMacro = null;
            return;
        }

        // 每个触发器持有独立步骤副本，运行中的任务不会观察到后续编辑或发布。
        trigger.RuntimeMacro = macro.DeepClone();
        trigger.MacroSteps = macro.Steps.Select(step => step.DeepClone()).ToList();
    }
}
