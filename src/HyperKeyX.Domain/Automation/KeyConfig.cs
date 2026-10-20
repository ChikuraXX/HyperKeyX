namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 单个按键的连发和宏行为配置
/// </summary>
public class KeyConfig : TriggerBehaviorConfig
{
    public int TargetVirtualKey { get; set; }
    public int TargetScanCode { get; set; }
    public bool IsExtendedKey { get; set; }

    public KeyConfig DeepClone()
    {
        return CopyBehaviorTo(new KeyConfig
        {
            TargetVirtualKey = TargetVirtualKey,
            TargetScanCode = TargetScanCode,
            IsExtendedKey = IsExtendedKey
        });
    }
}
