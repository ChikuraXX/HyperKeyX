using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record MousePathPreview(IReadOnlyList<PathPosition> Points, long? DurationUs, IReadOnlyList<PathIssue> Issues,
    IReadOnlyList<PathPosition>? RawPoints = null, IReadOnlyList<Guid>? KeyPointIds = null);

public interface IMousePathPreviewBuilder
{
    Task<MousePathPreview> BuildAsync(MacroDefinition macro, int stepIndex, CancellationToken token);
}

/// <summary>只使用保存的坐标框和固定示例随机源，不读取或操作真实桌面。</summary>
public sealed class MousePathPreviewBuilder : IMousePathPreviewBuilder
{
    public Task<MousePathPreview> BuildAsync(MacroDefinition macro, int stepIndex, CancellationToken token)
    {
        var snapshot = macro.DeepClone();
        return Task.Run(() => Build(snapshot, stepIndex, token), token);
    }

    private static MousePathPreview Build(MacroDefinition macro, int stepIndex, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stepIndex < 0 || stepIndex >= macro.Steps.Count || macro.Steps[stepIndex].MousePath is not { } selected)
            return new([], null, [new(PathIssueCode.MissingPayload)]);
        var validation = MousePathValidator.Validate(macro);
        if (!validation.IsValid) return new([], null, validation.Issues);
        var group = macro.PathGroups.Single(g => g.GroupId == selected.GroupId);
        var frame = group.RecordedFrame;
        if (frame.Left < int.MinValue || frame.Left > int.MaxValue || frame.Top < int.MinValue || frame.Top > int.MaxValue ||
            frame.Width < 1 || frame.Width > int.MaxValue || frame.Height < 1 || frame.Height > int.MaxValue)
            return new([], null, [new(PathIssueCode.InvalidGroup)]);
        var rectangle = new PathPixelRectangle((int)frame.Left, (int)frame.Top, (int)frame.Width, (int)frame.Height);
        var context = new PathCoordinateContext([rectangle], group.Origin,
            group.CoordinateMode == MousePathCoordinateMode.WindowClient ? rectangle : null);
        var indices = macro.Steps.Select((s, i) => (s, i)).Where(p => p.s.MousePath?.GroupId == group.GroupId).Select(p => p.i).ToArray();
        var rawStride = Math.Max(1, (int)Math.Ceiling(selected.Source.Points.Count / 2_000d));
        var raw = selected.Source.Points.Where((_, i) => i % rawStride == 0).Append(selected.Source.Points[^1])
            .Select(p => MousePathCoordinateResolver.Resolve(new(p.X, p.Y), group, context))
            .Where(p => p.IsValid).Select(p => p.Position).ToArray();
        IReadOnlyList<Guid>? keyIds = null;
        try
        {
            var inputs = indices.Select(i => new MousePathPlanningInput(
                MousePathProcessor.Process(macro.Steps[i].MousePath!, group, token), macro.Steps[i].MousePath!)).ToArray();
            keyIds = inputs[Array.IndexOf(indices, stepIndex)].Processed.Points.Select(p => p.PointId).ToArray();
            var compiled = MousePathPlanCompiler.Compile(inputs, group, context, new ExampleRandom(), token);
            var timeline = compiled.Timelines[Array.IndexOf(indices, stepIndex)];
            var stride = Math.Max(1, (int)Math.Ceiling(timeline.Commands.Count / 2_000d));
            var display = timeline.Commands.Where((_, i) => i % stride == 0).Select(p => p.Position)
                .Append(timeline.Commands[^1].Position).ToArray();
            return new(Array.AsReadOnly(display), timeline.DurationUs, [], Array.AsReadOnly(raw), keyIds);
        }
        catch (MousePathExecutionException exception)
        {
            return new([], null, exception.Issues.Select(i => i.StepIndex >= 0 && i.StepIndex < indices.Length
                ? i with { StepIndex = indices[i.StepIndex] } : i).ToArray(), Array.AsReadOnly(raw), keyIds);
        }
    }

    private sealed class ExampleRandom : IPathRandomSource
    {
        private readonly Random _random = new(731);
        public double NextUnit() => _random.NextDouble();
    }
}
