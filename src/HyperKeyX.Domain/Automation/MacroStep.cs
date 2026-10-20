using HyperKeyX.Domain.Automation.Paths;
using System.Text.Json.Serialization;

namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 表示宏序列中的单个步骤
/// </summary>
public class MacroStep
{
    public MacroActionType ActionType { get; set; }
    public int VirtualKeyCode { get; set; }
    public int ScanCode { get; set; }
    public bool IsExtendedKey { get; set; }
    public int DurationMs { get; set; }
    /// <summary>随机延时上限（毫秒，含端点）；空值表示固定延时，启用时 DurationMs 为下限。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RandomDurationMaxMs { get; set; }
    /// <summary>随机延时分布，仅在设置了随机上限时生效。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MacroDelayDistribution DelayDistribution { get; set; }
    public MouseButtonKind MouseButton { get; set; }
    public int WheelDelta { get; set; }
    public MousePathDefinition? MousePath { get; set; }

    public MacroStep DeepClone() => new()
    {
        ActionType = ActionType,
        VirtualKeyCode = VirtualKeyCode,
        ScanCode = ScanCode,
        IsExtendedKey = IsExtendedKey,
        DurationMs = DurationMs,
        RandomDurationMaxMs = RandomDurationMaxMs,
        DelayDistribution = DelayDistribution,
        MouseButton = MouseButton,
        WheelDelta = WheelDelta,
        MousePath = MousePath?.DeepClone()
    };

    public override string ToString()
    {
        return ActionType switch
        {
            MacroActionType.KeyDown => $"KeyDown VK={VirtualKeyCode} ({DurationMs}ms)",
            MacroActionType.KeyUp => $"KeyUp VK={VirtualKeyCode} ({DurationMs}ms)",
            MacroActionType.Delay => $"Delay {DurationMs}ms",
            MacroActionType.MouseButtonDown => $"MouseButtonDown {MouseButton}",
            MacroActionType.MouseButtonUp => $"MouseButtonUp {MouseButton}",
            MacroActionType.MouseWheel => $"MouseWheel {WheelDelta}",
            MacroActionType.MousePath => $"MousePath {MousePath?.Source.Points.Count ?? 0} points",
            _ => $"Unknown {ActionType}"
        };
    }
}
