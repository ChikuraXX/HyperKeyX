namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>只比较持久化语义，不比较宏身份或临时播放状态。</summary>
public static class MacroContentComparer
{
    public static bool Equals(MacroDefinition left, MacroDefinition right) =>
        left.Name == right.Name && left.Description == right.Description &&
        left.WindowTargets.SequenceEqual(right.WindowTargets) &&
        left.PathGroups.Count == right.PathGroups.Count &&
        left.PathGroups.Zip(right.PathGroups).All(pair => GroupEquals(pair.First, pair.Second)) &&
        left.Steps.Count == right.Steps.Count &&
        left.Steps.Zip(right.Steps).All(pair => StepEquals(pair.First, pair.Second));

    public static bool StepEquals(MacroStep left, MacroStep right)
    {
        if (left.ActionType != right.ActionType || left.DurationMs != right.DurationMs ||
            left.RandomDurationMaxMs != right.RandomDurationMaxMs || left.DelayDistribution != right.DelayDistribution)
            return false;

        if (!PathEquals(left.MousePath, right.MousePath))
            return false;

        // 旧动作中未使用的历史字段不影响已有导入去重语义。
        return left.ActionType switch
        {
            MacroActionType.KeyDown or MacroActionType.KeyUp =>
                left.VirtualKeyCode == right.VirtualKeyCode &&
                left.ScanCode == right.ScanCode && left.IsExtendedKey == right.IsExtendedKey,
            MacroActionType.MouseButtonDown or MacroActionType.MouseButtonUp =>
                left.MouseButton == right.MouseButton,
            MacroActionType.MouseWheel => left.WheelDelta == right.WheelDelta,
            MacroActionType.Delay or MacroActionType.MousePath => true,
            _ => false
        };
    }

    private static bool PathEquals(MousePathDefinition? left, MousePathDefinition? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.GroupId == right.GroupId && left.Mode == right.Mode &&
               left.SourceKind == right.SourceKind && left.Processing == right.Processing &&
               left.Timing == right.Timing && left.Edits.SequenceEqual(right.Edits) &&
               (ReferenceEquals(left.Source, right.Source) || left.Source.Points.SequenceEqual(right.Source.Points));
    }

    private static bool GroupEquals(MousePathGroup left, MousePathGroup right) =>
        left.GroupId == right.GroupId && left.CoordinateMode == right.CoordinateMode &&
        left.TargetId == right.TargetId && left.Origin == right.Origin &&
        left.RecordedFrame == right.RecordedFrame && left.Randomization == right.Randomization;
}
