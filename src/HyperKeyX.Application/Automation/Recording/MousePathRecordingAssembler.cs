using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public sealed record MousePathCaptureResult(MacroDefinition Macro, IReadOnlyList<PathIssue> Issues,
    IReadOnlyList<MacroValidationIssue> SequenceIssues, long EffectiveCutoffUs)
{
    public bool IsValid => Issues.Count == 0 && SequenceIssues.Count == 0;
}

/// <summary>停止后在钩子外分段；原始事件只用于生成独立源块，不生成随机结果。</summary>
public static class MousePathRecordingAssembler
{
    public static MousePathCaptureResult Assemble(MousePathRecordingContext initialContext,
        IEnumerable<CapturedMacroEvent> events, long cutoffUs, CancellationToken token,
        int maximumPoints = MousePathLimits.MaximumMacroPoints,
        int maximumSteps = MacroSequenceValidator.MaximumStepCount,
        long maximumDurationUs = MousePathLimits.MaximumRecordingDurationUs)
    {
        token.ThrowIfCancellationRequested();
        var context = initialContext.DeepClone();
        var macro = new MacroDefinition { MacroId = Guid.NewGuid(), Name = "录制宏" };
        var issues = new List<PathIssue>();
        if (maximumPoints is <= 0 or > MousePathLimits.MaximumMacroPoints ||
            maximumSteps is <= 0 or > MacroSequenceValidator.MaximumStepCount ||
            maximumDurationUs is < 0 or > MousePathLimits.MaximumRecordingDurationUs)
            throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        if (cutoffUs > maximumDurationUs)
        {
            cutoffUs = maximumDurationUs;
            issues.Add(new(PathIssueCode.CaptureDurationLimitExceeded));
        }
        var heldKeys = new HashSet<int>();
        var heldButtons = new HashSet<MouseButtonKind>();
        var group = CreateGroup(context, macro);
        var position = context.InitialPosition;
        var fragmentStart = 0L;
        var points = new List<MousePathPoint> { Point(position, 0, 0, Guid.NewGuid(), context) };
        var storedPoints = 0;
        var lastAcceptedTime = 0L;
        var fragmentOpen = true;
        var limitReached = false;
        var received = new List<CapturedMacroEvent>();
        foreach (var input in events)
        {
            if ((received.Count & 1023) == 0) token.ThrowIfCancellationRequested();
            if (received.Count >= maximumPoints + maximumSteps * 2 + 2)
            {
                issues.Add(new(PathIssueCode.TooManyPoints));
                break;
            }
            received.Add(input);
        }
        received.Sort((a, b) => a.TimestampUs != b.TimestampUs ? a.TimestampUs.CompareTo(b.TimestampUs)
            : (a.SourceSequence > 0 ? a.SourceSequence : a.Sequence).CompareTo(b.SourceSequence > 0 ? b.SourceSequence : b.Sequence));

        foreach (var input in received)
        {
            token.ThrowIfCancellationRequested();
            if (input.TimestampUs > cutoffUs) break;
            if (input.TimestampUs < 0 || !Enum.IsDefined(input.Kind))
            {
                issues.Add(new(PathIssueCode.InvalidTimeOrder));
                cutoffUs = lastAcceptedTime;
                break;
            }
            if (macro.Steps.Count >= maximumSteps)
            {
                issues.Add(new(PathIssueCode.CaptureStepLimitExceeded));
                cutoffUs = lastAcceptedTime;
                break;
            }
            if (input.Kind == CapturedMacroEventKind.ExcludedBoundary)
            {
                cutoffUs = input.TimestampUs;
                issues.Add(new(PathIssueCode.TargetChanged));
                break;
            }
            if (input.Kind == CapturedMacroEventKind.ContextChanged)
            {
                if (heldKeys.Count > 0 || heldButtons.Count > 0 || input.Context is null)
                {
                    issues.Add(new(PathIssueCode.TargetSwitchWhileHeld));
                    cutoffUs = input.TimestampUs;
                    break;
                }
                CloseFragment(input.TimestampUs, false);
                if (limitReached) break;
                context = input.Context.DeepClone();
                position = input.Position ?? context.InitialPosition;
                context = context with { InitialPosition = position };
                group = CreateGroup(context, macro);
                fragmentStart = input.TimestampUs;
                points = [Point(position, 0, 0, Guid.NewGuid(), context)];
                fragmentOpen = true;
                lastAcceptedTime = input.TimestampUs;
                continue;
            }
            var previousPosition = position;
            if (input.Position is { } capturedPosition)
            {
                if (!PathGeometry.IsFinite(capturedPosition))
                {
                    issues.Add(new(PathIssueCode.InvalidCoordinate));
                    cutoffUs = input.TimestampUs;
                    break;
                }
                position = capturedPosition;
            }
            if (input.Kind == CapturedMacroEventKind.Move)
            {
                if (storedPoints + points.Count >= maximumPoints)
                {
                    position = previousPosition;
                    cutoffUs = lastAcceptedTime;
                    issues.Add(new(PathIssueCode.TooManyPoints));
                    break;
                }
                points.Add(Point(position, input.TimestampUs - fragmentStart, points.Count, null, context));
                lastAcceptedTime = input.TimestampUs;
                continue;
            }

            if (macro.Steps.Count + 2 > maximumSteps)
            {
                position = previousPosition;
                cutoffUs = lastAcceptedTime;
                issues.Add(new(PathIssueCode.CaptureStepLimitExceeded));
                break;
            }

            // 键盘没有未来位置可借用；输入边界使用此前已接收的位置，鼠标则使用事件自带位置。
            var anchor = CloseFragment(input.TimestampUs, true);
            if (limitReached) break;
            var step = CreateStep(input);
            macro.Steps.Add(step);
            if (input.Kind == CapturedMacroEventKind.KeyDown) heldKeys.Add(step.VirtualKeyCode);
            if (input.Kind == CapturedMacroEventKind.KeyUp) heldKeys.Remove(step.VirtualKeyCode);
            if (input.Kind == CapturedMacroEventKind.ButtonDown) heldButtons.Add(step.MouseButton);
            if (input.Kind == CapturedMacroEventKind.ButtonUp) heldButtons.Remove(step.MouseButton);
            fragmentStart = input.TimestampUs;
            points = [Point(position, 0, 0, anchor, context)];
            fragmentOpen = true;
            lastAcceptedTime = input.TimestampUs;
        }
        CloseFragment(cutoffUs, false);
        // 无实际片段的末尾上下文不留下悬空定义。
        var usedGroups = macro.Steps.Where(s => s.MousePath is not null).Select(s => s.MousePath!.GroupId).ToHashSet();
        macro.PathGroups.RemoveAll(g => !usedGroups.Contains(g.GroupId));
        var usedTargets = macro.PathGroups.Where(g => g.TargetId is not null).Select(g => g.TargetId!.Value).ToHashSet();
        macro.WindowTargets.RemoveAll(t => !usedTargets.Contains(t.TargetId));
        issues.AddRange(MousePathValidator.Validate(macro).Issues);
        var sequence = MacroSequenceValidator.Validate(macro.Steps, false);
        return new(macro, issues.AsReadOnly(), sequence.Issues, cutoffUs);

        Guid CloseFragment(long time, bool requiredPositioning)
        {
            if (!fragmentOpen) return points[^1].AnchorId!.Value;
            var last = points[^1];
            var end = Point(position, time - fragmentStart, points.Count, Guid.NewGuid(), context);
            if (last.OffsetUs == end.OffsetUs && last.X == end.X && last.Y == end.Y)
                points[^1] = last with { AnchorId = last.AnchorId ?? end.AnchorId };
            else if (storedPoints + points.Count < maximumPoints)
                points.Add(end);
            else
            {
                points[^1] = last with { AnchorId = last.AnchorId ?? end.AnchorId };
                cutoffUs = fragmentStart + last.OffsetUs;
                issues.Add(new(PathIssueCode.TooManyPoints));
                limitReached = true;
            }
            fragmentOpen = false;
            if (requiredPositioning || points.Count > 1 || points[^1].OffsetUs > 0)
            {
                if (macro.Steps.Count >= maximumSteps || storedPoints + points.Count > maximumPoints)
                {
                    issues.Add(new(macro.Steps.Count >= maximumSteps ? PathIssueCode.CaptureStepLimitExceeded : PathIssueCode.TooManyPoints));
                    cutoffUs = lastAcceptedTime;
                    limitReached = true;
                    return points[^1].AnchorId!.Value;
                }
                macro.Steps.Add(new MacroStep
                {
                    ActionType = MacroActionType.MousePath,
                    MousePath = new()
                    {
                        GroupId = group.GroupId, Mode = context.Mode, SourceKind = MousePathSourceKind.Recorded,
                        Source = new(points), Processing = context.Processing is null ? new() : context.Processing with { },
                        Timing = context.Timing is null ? new() : context.Timing with { }
                    }
                });
                storedPoints += points.Count;
            }
            return points[^1].AnchorId!.Value;
        }
    }

    private static MousePathGroup CreateGroup(MousePathRecordingContext context, MacroDefinition macro)
    {
        var group = new MousePathGroup
        {
            GroupId = Guid.NewGuid(), CoordinateMode = context.CoordinateMode, Origin = context.InitialPosition,
            TargetId = context.Target?.TargetId, RecordedFrame = context.RecordedFrame with { },
            Randomization = context.Randomization is null ? new() : context.Randomization with { }
        };
        macro.PathGroups.Add(group);
        if (context.Target is not null && macro.WindowTargets.All(t => t.TargetId != context.Target.TargetId))
            macro.WindowTargets.Add(context.Target.DeepClone());
        return group;
    }

    private static MousePathPoint Point(PathPosition physical, long time, long sequence, Guid? anchor,
        MousePathRecordingContext context)
    {
        var frame = context.RecordedFrame;
        var position = context.CoordinateMode switch
        {
            MousePathCoordinateMode.RelativeStart => new PathPosition(physical.X - context.InitialPosition.X, physical.Y - context.InitialPosition.Y),
            MousePathCoordinateMode.WindowClient => new PathPosition(
                (physical.X - frame.Left) / Math.Max(1, frame.Width - 1),
                (physical.Y - frame.Top) / Math.Max(1, frame.Height - 1)),
            _ => physical
        };
        return new(Guid.NewGuid(), time, sequence, position.X, position.Y, anchor);
    }

    private static MacroStep CreateStep(CapturedMacroEvent input) => new()
    {
        ActionType = input.Kind switch
        {
            CapturedMacroEventKind.KeyDown => MacroActionType.KeyDown,
            CapturedMacroEventKind.KeyUp => MacroActionType.KeyUp,
            CapturedMacroEventKind.ButtonDown => MacroActionType.MouseButtonDown,
            CapturedMacroEventKind.ButtonUp => MacroActionType.MouseButtonUp,
            CapturedMacroEventKind.Wheel => MacroActionType.MouseWheel,
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        },
        VirtualKeyCode = input.Key?.VirtualKeyCode ?? 0, ScanCode = input.Key?.ScanCode ?? 0,
        IsExtendedKey = input.Key?.IsExtendedKey ?? false, MouseButton = input.Button, WheelDelta = input.WheelDelta
    };
}
