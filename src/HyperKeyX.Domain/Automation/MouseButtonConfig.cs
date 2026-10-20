namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 单个鼠标按钮的连点和宏行为配置。
/// </summary>
public sealed class MouseButtonConfig : TriggerBehaviorConfig
{
    public MouseButtonConfig()
    {
        PassThroughOriginalInput = true;
    }

    public MouseButtonConfig DeepClone() => CopyBehaviorTo(new MouseButtonConfig());
}
