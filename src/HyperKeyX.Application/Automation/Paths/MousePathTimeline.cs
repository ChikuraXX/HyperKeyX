using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

/// <summary>不可变发送时间线；准备时检查名义量化运动，发送时再次检查实际时刻。</summary>
public sealed class MousePathTimeline
{
    private readonly MousePathTimingSettings _timing;
    private MousePathTimeline(List<TimedPathPosition> commands, MousePathTimingSettings timing)
    {
        Commands = commands.AsReadOnly(); _timing = timing with { };
    }
    public IReadOnlyList<TimedPathPosition> Commands { get; }
    public long DurationUs => Commands[^1].OffsetUs;

    public static MousePathTimeline Prepare(PathPlan plan, MousePathDefinition path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!plan.IsValid) throw new MousePathExecutionException(plan.Issues);
        var validation = MousePathValidator.ValidatePayload(path);
        if (!validation.IsValid) throw new MousePathExecutionException(validation.Issues);
        if (plan.Points.Count == 0) Fail(PathIssueCode.EmptySource);
        // 移动轨迹无法在有限量化采样下证明零误差，不能偷偷退回默认采样间隔。
        if (path.Processing.SimplificationMaxDeviationPx == 0 && plan.Segments.Any(s => !s.IsHold))
            Fail(PathIssueCode.MotionLimitExceeded);
        var mandatory = plan.Points.Where(p => p.IsProtected || p.IsHoldBoundary).Select(p => p.OffsetUs)
            .Append(0).Append(plan.DurationUs).Distinct().Order().ToArray();
        var commands = new List<TimedPathPosition>();
        Add(0);
        for (var index = 1; index < mandatory.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var start = mandatory[index - 1];
            var duration = mandatory[index] - start;
            // 加速度给出线性采样误差上界 a·dt²/8；像素量化由后续运动审计另行计入。
            var budget = path.Processing.SimplificationMaxDeviationPx;
            var maxInterval = budget > 0
                ? Math.Min(8_000d, Math.Sqrt(8 * budget / path.Timing.MaxAccelerationPxPerSecondSquared) * 1_000_000)
                : 8_000d;
            if (maxInterval < 1 || !double.IsFinite(maxInterval)) Fail(PathIssueCode.MotionLimitExceeded);
            var count = Math.Max(1L, (long)Math.Ceiling(duration / maxInterval));
            if (count > MousePathLimits.MaximumMacroPoints - commands.Count) Fail(PathIssueCode.TooManyPoints);
            for (var sample = 1L; sample <= count; sample++)
            {
                if ((sample & 1023) == 0) token.ThrowIfCancellationRequested();
                Add(start + (long)Math.Round((double)duration * sample / count, MidpointRounding.AwayFromZero));
            }
        }
        var audit = new PathMotionSampleAudit(path.Timing);
        foreach (var command in commands)
        {
            token.ThrowIfCancellationRequested();
            ThrowIfIssue(audit.Accept(command));
        }
        ThrowIfIssue(audit.Complete());
        return new(commands, path.Timing);

        void Add(long time) => commands.Add(new(time, MousePathCoordinateResolver.Quantize(plan.PositionAt(time))));
    }

    public async Task PlayAsync(IMousePathClock clock, IPointerInputService pointer,
        Func<PathIssue?> guard, CancellationToken token, long? originUs = null)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfIssue(guard());
        var lastPosition = pointer.GetPosition();
        if (lastPosition != Commands[0].Position) Fail(PathIssueCode.InvalidCoordinate);
        var origin = originUs ?? clock.NowUs;
        if (clock.NowUs - origin > MousePathLimits.MaximumSchedulingLatenessUs) Fail(PathIssueCode.SchedulingOverrun);
        var audit = new PathMotionSampleAudit(_timing);
        ThrowIfIssue(audit.Accept(new(Math.Max(0, clock.NowUs - origin), lastPosition)));
        for (var index = 1; index < Commands.Count; index++)
        {
            var command = Commands[index];
            await clock.DelayUntilAsync(checked(origin + command.OffsetUs), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ThrowIfIssue(guard());
            token.ThrowIfCancellationRequested();
            var now = clock.NowUs - origin;
            if (now < command.OffsetUs || now - command.OffsetUs > MousePathLimits.MaximumSchedulingLatenessUs ||
                (index + 1 < Commands.Count && now >= Commands[index + 1].OffsetUs))
                Fail(PathIssueCode.SchedulingOverrun);
            if (pointer.GetPosition() != lastPosition) Fail(PathIssueCode.TargetChanged);
            ThrowIfIssue(audit.Check(new(now, command.Position)));
            if (command.Position != lastPosition) pointer.MoveTo(command.Position);
            var sentAt = clock.NowUs - origin;
            ThrowIfIssue(audit.Accept(new(sentAt, command.Position)));
            if (sentAt - command.OffsetUs > MousePathLimits.MaximumSchedulingLatenessUs)
                Fail(PathIssueCode.SchedulingOverrun);
            lastPosition = command.Position;
        }
        token.ThrowIfCancellationRequested();
        ThrowIfIssue(guard());
        ThrowIfIssue(audit.Complete());
    }

    internal static void ThrowIfIssue(PathIssue? issue)
    {
        if (issue is not null) throw new MousePathExecutionException([issue]);
    }
    private static void Fail(PathIssueCode code) => throw new MousePathExecutionException([new(code)]);
}
