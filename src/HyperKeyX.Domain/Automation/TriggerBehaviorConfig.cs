using System.Text.Json.Serialization;

namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 键盘与鼠标触发器共用的连发和宏行为配置。
/// </summary>
public abstract class TriggerBehaviorConfig
{
    public bool IsTurboEnabled { get; set; }
    public int KeyDownDuration { get; set; } = 100;
    public int KeyUpDuration { get; set; } = 100;
    public int TriggerMode { get; set; }
    public int LongPressThresholdMs { get; set; } = 500;
    public bool PassThroughOriginalInput { get; set; }
    public bool IsMacroEnabled { get; set; }
    public Guid? MacroId { get; set; }
    public List<MacroStep> MacroSteps { get; set; } = new();
    [JsonIgnore]
    public MacroDefinition? RuntimeMacro { get; set; }

    protected T CopyBehaviorTo<T>(T target) where T : TriggerBehaviorConfig
    {
        target.IsTurboEnabled = IsTurboEnabled;
        target.KeyDownDuration = KeyDownDuration;
        target.KeyUpDuration = KeyUpDuration;
        target.TriggerMode = TriggerMode;
        target.LongPressThresholdMs = LongPressThresholdMs;
        target.PassThroughOriginalInput = PassThroughOriginalInput;
        target.IsMacroEnabled = IsMacroEnabled;
        target.MacroId = MacroId;
        target.RuntimeMacro = RuntimeMacro?.DeepClone();
        target.MacroSteps = (MacroSteps ?? new List<MacroStep>())
            .Where(step => step != null)
            .Select(step => step.DeepClone())
            .ToList();
        return target;
    }

}
