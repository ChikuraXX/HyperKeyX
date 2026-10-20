using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 在不同配置文件中唯一标识一个触发器草稿。
/// </summary>
public readonly record struct TriggerDraftIdentity(
    Guid ProfileId,
    InputTriggerIdentity Trigger);

/// <summary>
/// 保存触发器值的深复制快照，不持有任何 ViewModel 引用。
/// </summary>
public sealed class TriggerDraftSnapshot
{
    private readonly TriggerBehaviorConfig _config;

    public TriggerDraftSnapshot(
        Guid profileId,
        InputTriggerIdentity trigger,
        TriggerBehaviorConfig config,
        bool isIncludedInSave = true)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!trigger.IsValid)
            throw new ArgumentOutOfRangeException(nameof(trigger));

        _config = (trigger.Kind, config) switch
        {
            (InputTriggerKind.Keyboard, KeyConfig key) => key.DeepClone(),
            (InputTriggerKind.MouseButton, MouseButtonConfig mouse) =>
                mouse.DeepClone(),
            _ => throw new ArgumentException(
                "The trigger kind does not match its configuration value.",
                nameof(config))
        };

        Identity = new TriggerDraftIdentity(profileId, trigger);
        IsIncludedInSave = isIncludedInSave;
    }

    public TriggerDraftIdentity Identity { get; }
    public Guid ProfileId => Identity.ProfileId;
    public InputTriggerIdentity Trigger => Identity.Trigger;
    public bool IsIncludedInSave { get; }
    public Guid? MacroId => _config.MacroId;

    internal TriggerBehaviorConfig CreateConfigSnapshot()
    {
        TriggerBehaviorConfig clone = _config switch
        {
            KeyConfig key => key.DeepClone(),
            MouseButtonConfig mouse => mouse.DeepClone(),
            _ => throw new InvalidOperationException(
                "The trigger snapshot contains an unsupported value.")
        };

        // 新文档只保存宏引用，运行时步骤由发布解析器统一物化。
        clone.MacroSteps = [];
        clone.RuntimeMacro = null;
        return clone;
    }
}
