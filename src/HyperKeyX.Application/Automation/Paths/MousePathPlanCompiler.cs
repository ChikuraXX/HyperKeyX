using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record CompiledPathGroup(IReadOnlyList<PathPlan> Plans, IReadOnlyList<MousePathTimeline> Timelines);

/// <summary>预览和执行共用的纯数据准备流程，不持有输入发送服务。</summary>
public static class MousePathPlanCompiler
{
    public static CompiledPathGroup Compile(IReadOnlyList<MousePathPlanningInput> inputs, MousePathGroup group,
        PathCoordinateContext context, IPathRandomSource random, CancellationToken token,
        Action<PathPosition>? validatePosition = null)
    {
        var planning = inputs.Select(input =>
        {
            var definition = input.Definition.DeepClone();
            if (definition.Timing.Mode == MousePathTimingMode.ConstantSpeed)
            {
                // 恒速允许受限加减速；给最终像素取整保留余量，原始/固定时间不改时长。
                definition.Timing.MaxAccelerationPxPerSecondSquared *= .25;
                definition.Timing.MaxSpeedPxPerSecond *= .5;
            }
            return new MousePathPlanningInput(input.Processed, definition);
        }).ToArray();
        var timelines = new List<MousePathTimeline>();
        var planned = MousePathPlanner.PlanGroup(planning, group, context, random, token, ValidateCandidate);
        if (!planned.IsValid) throw new MousePathExecutionException(planned.Issues);
        return new(planned.Plans, timelines.AsReadOnly());

        IReadOnlyList<PathIssue> ValidateCandidate(IReadOnlyList<PathPlan> plans)
        {
            timelines.Clear();
            var totalCommands = 0;
            for (var index = 0; index < plans.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var timeline = MousePathTimeline.Prepare(plans[index], inputs[index].Definition, token);
                    totalCommands += timeline.Commands.Count;
                    if (totalCommands > MousePathLimits.MaximumMacroPoints) return [new(PathIssueCode.TooManyPoints, index)];
                    if (validatePosition is not null)
                        foreach (var command in timeline.Commands)
                        {
                            token.ThrowIfCancellationRequested();
                            validatePosition(command.Position);
                        }
                    timelines.Add(timeline);
                }
                catch (MousePathExecutionException exception) { return exception.Issues.Select(i => i with { StepIndex = index }).ToArray(); }
                catch (ArgumentException) { return [new(PathIssueCode.UnreachablePosition, index)]; }
                catch (InvalidOperationException) { return [new(PathIssueCode.UnreachablePosition, index)]; }
            }
            return [];
        }
    }
}
