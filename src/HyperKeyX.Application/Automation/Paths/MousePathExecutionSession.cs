using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

/// <summary>固定窗口绑定属于触发会话；坐标起点、窗口几何和随机候选属于每轮的组。</summary>
internal sealed class MousePathExecutionSession : IDisposable
{
    private readonly MacroDefinition _macro;
    private readonly OwnedInputState _owned;
    private readonly IMousePathClock _clock;
    private readonly IPointerInputService _pointer;
    private readonly IWindowTargetService _windows;
    private readonly IPathRandomSource _random;
    private readonly WindowTargetSession _targets;
    private readonly Dictionary<int, MousePathProcessingResult> _processed = [];
    private readonly Dictionary<Guid, PreparedGroup> _groups = [];
    private PreparedGroup? _current;
    private Exception? _failure;
    private bool _disposed;
    private long _deadline;

    internal MousePathExecutionSession(MacroDefinition macro, OwnedInputState owned, IMousePathClock clock,
        IPointerInputService pointer, IWindowTargetService windows, IPathRandomSource random, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var validation = MousePathValidator.Validate(macro);
        if (!validation.IsValid) throw new MousePathExecutionException(validation.Issues);
        if (!MacroSequenceValidator.Validate(macro.Steps, false).IsValid)
            throw new MousePathExecutionException([new(PathIssueCode.InvalidStructure)]);
        _macro = macro.DeepClone(); _owned = owned; _clock = clock; _pointer = pointer; _windows = windows; _random = random;
        foreach (var (step, index) in _macro.Steps.Select((s, i) => (s, i)))
        {
            token.ThrowIfCancellationRequested();
            if (step.MousePath is not { } path) continue;
            var group = _macro.PathGroups.Single(g => g.GroupId == path.GroupId);
            var result = MousePathProcessor.Process(path, group, token);
            if (!result.IsValid) throw new MousePathExecutionException(result.Issues.Select(i => i with { StepIndex = index }));
            _processed.Add(index, result);
        }
        var usedGroups = _macro.Steps.Where(s => s.MousePath is not null).Select(s => s.MousePath!.GroupId).ToHashSet();
        var usedTargets = _macro.PathGroups.Where(g => usedGroups.Contains(g.GroupId) && g.TargetId is not null).Select(g => g.TargetId).ToHashSet();
        var resolution = new WindowTargetResolver(windows).BindAll(_macro.WindowTargets.Where(t => usedTargets.Contains(t.TargetId)).ToArray(), token);
        if (!resolution.IsValid) throw new MousePathExecutionException(resolution.Issues);
        _targets = resolution.Session!;
    }

    public IReadOnlyList<PathIssue> CleanupIssues => _targets.CleanupIssues;

    public async Task PlayCycleAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null) throw new InvalidOperationException("轨迹会话已经停止，不能重试下一轮。", _failure);
        try
        {
            token.ThrowIfCancellationRequested();
            MousePathTimeline.ThrowIfIssue(_targets.BeginCycle(_owned.HasHeldInput));
            _groups.Clear(); _current = null; _deadline = _clock.NowUs;
            for (var index = 0; index < _macro.Steps.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var step = _macro.Steps[index];
                if (step.MousePath is { } path)
                {
                    var firstEntry = !_groups.TryGetValue(path.GroupId, out var group);
                    if (firstEntry)
                    {
                        // 组不能交叉返回；进入下一组后无需继续持有上一组的派生发送数组。
                        _groups.Clear();
                        group = PrepareGroup(path.GroupId, token);
                        _groups.Add(path.GroupId, group);
                    }
                    _current = group;
                    var timeline = group!.Timelines[index];
                    if (firstEntry)
                    {
                        await PositionForEntryAsync(timeline.Commands[0].Position, path, token).ConfigureAwait(false);
                        _deadline = _clock.NowUs;
                    }
                    await timeline.PlayAsync(_clock, _pointer, () => ValidateContext(), token, _deadline).ConfigureAwait(false);
                    _deadline = checked(_deadline + timeline.DurationUs);
                    group.LastPosition = timeline.Commands[^1].Position;
                    continue;
                }
                MousePathTimeline.ThrowIfIssue(ValidateContext(step.ActionType is MacroActionType.MouseButtonDown or MacroActionType.MouseButtonUp or MacroActionType.MouseWheel));
                token.ThrowIfCancellationRequested();
                switch (step.ActionType)
                {
                    case MacroActionType.KeyDown: _owned.KeyDown(KeyboardKeyIdentity.FromMacroStep(step)); break;
                    case MacroActionType.KeyUp: _owned.KeyUp(KeyboardKeyIdentity.FromMacroStep(step)); break;
                    case MacroActionType.MouseButtonDown: _owned.ButtonDown(step.MouseButton); break;
                    case MacroActionType.MouseButtonUp: _owned.ButtonUp(step.MouseButton); break;
                    case MacroActionType.MouseWheel: _owned.Wheel(step.WheelDelta); break;
                    case MacroActionType.Delay: break;
                    default: throw new MousePathExecutionException([new(PathIssueCode.InvalidEnum, index)]);
                }
                var delayMs = MacroDelay.ResolveMilliseconds(step);
                if (delayMs > 0) await WaitAsync(delayMs * 1_000L, token).ConfigureAwait(false);
            }
            if (_owned.HasHeldInput) throw new MousePathExecutionException([new(PathIssueCode.InvalidStructure)]);
            MousePathTimeline.ThrowIfIssue(ValidateContext());
        }
        catch (Exception exception) { _failure = exception; throw; }
    }

    private PreparedGroup PrepareGroup(Guid groupId, CancellationToken token)
    {
        var definition = _macro.PathGroups.Single(g => g.GroupId == groupId);
        WindowGroupGuard? guard = null;
        if (definition.TargetId is { } target)
        {
            var entered = _targets.EnterGroup(groupId, target, _owned.HasHeldInput);
            MousePathTimeline.ThrowIfIssue(entered.Issue);
            guard = entered.Guard;
        }
        var context = new PathCoordinateContext(Monitors(), _pointer.GetPosition(), guard?.Geometry.Client);
        var indices = _macro.Steps.Select((s, i) => (s, i)).Where(p => p.s.MousePath?.GroupId == groupId).Select(p => p.i).ToArray();
        try
        {
            var compiled = MousePathPlanCompiler.Compile(indices.Select(i => new MousePathPlanningInput(_processed[i], _macro.Steps[i].MousePath!)).ToArray(),
                definition, context, _random, token, p => _pointer.ValidatePosition(p, context.Monitors));
            return new(context, guard, indices.Select((i, local) => (i, compiled.Timelines[local])).ToDictionary(p => p.i, p => p.Item2));
        }
        catch (MousePathExecutionException exception)
        {
            throw new MousePathExecutionException(exception.Issues.Select(i => i.StepIndex >= 0 && i.StepIndex < indices.Length
                ? i with { StepIndex = indices[i.StepIndex] } : i));
        }
    }

    private async Task PositionForEntryAsync(PathPosition target, MousePathDefinition path, CancellationToken token)
    {
        var current = _pointer.GetPosition();
        if (current == target) return;
        if (_owned.HasHeldButtons) throw new MousePathExecutionException([new(PathIssueCode.InvalidCoordinate)]);
        var group = new MousePathGroup { GroupId = Guid.NewGuid(), CoordinateMode = MousePathCoordinateMode.VirtualDesktop,
            RecordedFrame = new() { Width = 1, Height = 1 } };
        var entry = new MousePathDefinition
        {
            GroupId = group.GroupId, SourceKind = MousePathSourceKind.Manual,
            Source = new([new(Guid.NewGuid(), 0, 0, current.X, current.Y, Guid.NewGuid()),
                new(Guid.NewGuid(), 1_000_000, 1, target.X, target.Y, Guid.NewGuid())]),
            Processing = new() { SmoothingEnabled = false, SimplificationEnabled = false },
            Timing = path.Timing with { Mode = MousePathTimingMode.ConstantSpeed }
        };
        var context = new PathCoordinateContext(_current!.Context.Monitors, current);
        var compiled = MousePathPlanCompiler.Compile([new(MousePathProcessor.Process(entry, group, token), entry)], group,
            context, _random, token, p => _pointer.ValidatePosition(p, context.Monitors));
        var timeline = compiled.Timelines[0];
        await timeline.PlayAsync(_clock, _pointer, () => ValidateContext(), token).ConfigureAwait(false);
    }

    private async Task WaitAsync(long durationUs, CancellationToken token)
    {
        _deadline = checked(_deadline + durationUs);
        do
        {
            token.ThrowIfCancellationRequested();
            await _clock.DelayUntilAsync(Math.Min(_deadline, _clock.NowUs + 25_000), token).ConfigureAwait(false);
            MousePathTimeline.ThrowIfIssue(ValidateContext());
        } while (_clock.NowUs < _deadline);
        if (_clock.NowUs - _deadline > MousePathLimits.MaximumSchedulingLatenessUs)
            throw new MousePathExecutionException([new(PathIssueCode.SchedulingOverrun)]);
    }

    private PathIssue? ValidateContext(bool hitTest = false)
    {
        if (_targets.ValidateBindings() is { } failure) return failure;
        if (_current is null) return null;
        if (!_current.Context.Monitors.SequenceEqual(Monitors())) return new(PathIssueCode.TargetChanged);
        if (hitTest && _current.LastPosition is { } expected && _pointer.GetPosition() != expected)
            return new(PathIssueCode.TargetChanged);
        return _current.Guard?.Validate(hitTest ? _pointer.GetPosition() : null);
    }
    private PathPixelRectangle[] Monitors() => _windows.GetMonitors().OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Width).ThenBy(m => m.Height).ToArray();
    public void Dispose() { if (_disposed) return; _disposed = true; _targets.Dispose(); _groups.Clear(); }
    private sealed class PreparedGroup(PathCoordinateContext context, WindowGroupGuard? guard, Dictionary<int, MousePathTimeline> timelines)
    {
        public PathCoordinateContext Context { get; } = context;
        public WindowGroupGuard? Guard { get; } = guard;
        public Dictionary<int, MousePathTimeline> Timelines { get; } = timelines;
        public PathPosition? LastPosition { get; set; }
    }
}
