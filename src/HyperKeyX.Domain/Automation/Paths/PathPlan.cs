namespace HyperKeyX.Domain.Automation.Paths;

public readonly record struct PathHold(long StartUs, long DurationUs, PathPosition Position);

/// <summary>只存在于内存的不可变候选，时间使用相对微秒。</summary>
public sealed class PathPlan
{
    internal PathPlan(IEnumerable<ProcessedPathPoint> points, IEnumerable<PathMotionSpan> segments,
        IEnumerable<PathIssue> issues)
    {
        Points = Array.AsReadOnly(points.ToArray());
        Segments = Array.AsReadOnly(segments.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        var holds = new List<PathHold>();
        foreach (var segment in Segments.Where(s => s.IsHold))
        {
            if (holds.Count > 0 && holds[^1].StartUs + holds[^1].DurationUs == segment.StartUs &&
                holds[^1].Position == segment.First)
                holds[^1] = holds[^1] with { DurationUs = holds[^1].DurationUs + segment.EndUs - segment.StartUs };
            else
                holds.Add(new(segment.StartUs, segment.EndUs - segment.StartUs, segment.First));
        }
        Holds = holds.AsReadOnly();
    }

    public IReadOnlyList<ProcessedPathPoint> Points { get; }
    public IReadOnlyList<PathMotionSpan> Segments { get; }
    public IReadOnlyList<PathHold> Holds { get; }
    public IReadOnlyList<PathIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
    public long DurationUs => Points.Count > 0 ? Points[^1].OffsetUs : 0;

    public PathPosition PositionAt(long offsetUs)
    {
        if (!IsValid || Points.Count == 0)
            throw new InvalidOperationException("无可执行的轨迹计划。");
        if (offsetUs <= 0)
            return Points[0].Position;
        if (offsetUs >= DurationUs)
            return Points[^1].Position;
        var low = 0;
        var high = Segments.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Segments[middle].EndUs < offsetUs)
                low = middle + 1;
            else
                high = middle;
        }
        return Segments[low].PositionAt(offsetUs);
    }

    internal PathPosition VelocityAt(long offsetUs)
    {
        if (Segments.Count == 0) return default;
        var low = 0;
        var high = Segments.Count - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Segments[middle].EndUs < offsetUs) low = middle + 1;
            else high = middle;
        }
        return Segments[low].VelocityAt(offsetUs);
    }

    internal static PathPlan Failure(PathIssueCode code, Guid? pointId = null) =>
        new([], [], [new(code, PointId: pointId)]);
}

/// <summary>三次 Hermite 曲线，或直线上的梯形/三角速度曲线；可解析检查整段运动。</summary>
public sealed class PathMotionSpan
{
    private readonly PathPosition _control1;
    private readonly PathPosition _control2;
    private readonly double _rampSeconds;
    private readonly double _peakSpeed;
    private readonly bool _cruise;
    private double Seconds => (EndUs - StartUs) / 1_000_000d;

    private PathMotionSpan(long startUs, long endUs, PathPosition first, PathPosition last,
        PathPosition control1, PathPosition control2, bool cruise = false, double ramp = 0, double peak = 0)
    {
        StartUs = startUs; EndUs = endUs; First = first; Last = last;
        _control1 = control1; _control2 = control2;
        _cruise = cruise; _rampSeconds = ramp; _peakSpeed = peak;
    }

    public long StartUs { get; }
    public long EndUs { get; }
    public PathPosition First { get; }
    public PathPosition Last { get; }
    public bool IsHold => First == Last && _control1 == First && _control2 == Last;

    internal static PathMotionSpan Cubic(ProcessedPathPoint first, ProcessedPathPoint last,
        PathPosition firstVelocity, PathPosition lastVelocity)
    {
        var seconds = (last.OffsetUs - first.OffsetUs) / 1_000_000d;
        return new(first.OffsetUs, last.OffsetUs, first.Position, last.Position,
            Add(first.Position, Scale(firstVelocity, seconds / 3)),
            Subtract(last.Position, Scale(lastVelocity, seconds / 3)));
    }

    internal static PathMotionSpan Cruise(ProcessedPathPoint first, ProcessedPathPoint last,
        double maximumSpeed, double maximumAcceleration)
    {
        var distance = PathGeometry.Distance(first.Position, last.Position);
        if (distance == 0)
            return Cubic(first, last, default, default);
        var seconds = (last.OffsetUs - first.OffsetUs) / 1_000_000d;
        // 微秒向上取整后重新解速度曲线，不能沿用取整前的速度造成终点过冲。
        var ramp = distance <= maximumSpeed * maximumSpeed / maximumAcceleration
            ? seconds / 2
            : 2 * distance / (maximumAcceleration * (seconds + Math.Sqrt(Math.Max(0,
                seconds * seconds - 4 * distance / maximumAcceleration))));
        return new(first.OffsetUs, last.OffsetUs, first.Position, last.Position,
            first.Position, last.Position, true, ramp, distance / (seconds - ramp));
    }

    public PathPosition PositionAt(long offsetUs)
    {
        if (offsetUs <= StartUs) return First;
        if (offsetUs >= EndUs) return Last;
        var t = (offsetUs - StartUs) / 1_000_000d;
        if (_cruise)
        {
            var acceleration = _peakSpeed / _rampSeconds;
            var distance = t < _rampSeconds ? .5 * acceleration * t * t
                : t <= Seconds - _rampSeconds ? _peakSpeed * (t - _rampSeconds / 2)
                : PathGeometry.Distance(First, Last) - .5 * acceleration * Math.Pow(Seconds - t, 2);
            return PathGeometry.Lerp(First, Last, distance / PathGeometry.Distance(First, Last));
        }
        var u = t / Seconds;
        var a = PathGeometry.Lerp(First, _control1, u);
        var b = PathGeometry.Lerp(_control1, _control2, u);
        var c = PathGeometry.Lerp(_control2, Last, u);
        return PathGeometry.Lerp(PathGeometry.Lerp(a, b, u), PathGeometry.Lerp(b, c, u), u);
    }

    public PathPosition VelocityAt(long offsetUs)
    {
        if (Seconds <= 0 || IsHold) return default;
        var t = Math.Clamp((offsetUs - StartUs) / 1_000_000d, 0, Seconds);
        if (_cruise)
        {
            var speed = t < _rampSeconds ? _peakSpeed * t / _rampSeconds
                : t <= Seconds - _rampSeconds ? _peakSpeed : _peakSpeed * (Seconds - t) / _rampSeconds;
            return Scale(Subtract(Last, First), speed / PathGeometry.Distance(First, Last));
        }
        var u = t / Seconds;
        var a = Scale(Subtract(_control1, First), 3 / Seconds);
        var b = Scale(Subtract(_control2, _control1), 3 / Seconds);
        var c = Scale(Subtract(Last, _control2), 3 / Seconds);
        return PathGeometry.Lerp(PathGeometry.Lerp(a, b, u), PathGeometry.Lerp(b, c, u), u);
    }

    public PathPosition AccelerationAt(long offsetUs)
    {
        if (Seconds <= 0 || IsHold) return default;
        var t = Math.Clamp((offsetUs - StartUs) / 1_000_000d, 0, Seconds);
        if (_cruise)
        {
            var acceleration = t < _rampSeconds ? _peakSpeed / _rampSeconds
                : t < Seconds - _rampSeconds ? 0 : -_peakSpeed / _rampSeconds;
            return Scale(Subtract(Last, First), acceleration / PathGeometry.Distance(First, Last));
        }
        var first = Scale(Add(Subtract(_control2, Scale(_control1, 2)), First), 6 / (Seconds * Seconds));
        var last = Scale(Add(Subtract(Last, Scale(_control2, 2)), _control1), 6 / (Seconds * Seconds));
        return PathGeometry.Lerp(first, last, t / Seconds);
    }

    internal bool SatisfiesMotion(double speed, double acceleration)
    {
        if (Seconds <= 0) return IsHold;
        if (_cruise)
            return _peakSpeed <= speed * (1 + 1e-10) && _peakSpeed / _rampSeconds <= acceleration * (1 + 1e-10);
        // 导数 Bézier 控制向量的凸包给出全时域速度上界，加速度范数最大值位于端点。
        return PathGeometry.Distance(First, _control1) * 3 / Seconds <= speed &&
            PathGeometry.Distance(_control1, _control2) * 3 / Seconds <= speed &&
            PathGeometry.Distance(_control2, Last) * 3 / Seconds <= speed &&
            PathGeometry.Distance(default, AccelerationAt(StartUs)) <= acceleration &&
            PathGeometry.Distance(default, AccelerationAt(EndUs)) <= acceleration;
    }

    internal IReadOnlyList<PathPosition> Controls => [First, _control1, _control2, Last];
    internal static PathPosition Add(PathPosition a, PathPosition b) => new(a.X + b.X, a.Y + b.Y);
    internal static PathPosition Subtract(PathPosition a, PathPosition b) => new(a.X - b.X, a.Y - b.Y);
    internal static PathPosition Scale(PathPosition a, double value) => new(a.X * value, a.Y * value);
}
