namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>整数物理像素矩形；宽高包含首末像素。</summary>
public readonly record struct PathPixelRectangle(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0 &&
        (long)Left + Width - 1 <= int.MaxValue && (long)Top + Height - 1 <= int.MaxValue;
    public double Right => (double)Left + Width - 1;
    public double Bottom => (double)Top + Height - 1;
    public bool Contains(PathPosition point) => IsValid && PathGeometry.IsFinite(point) &&
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}

/// <summary>组入口冻结的坐标上下文，不引用实时窗口或可变显示器集合。</summary>
public sealed class PathCoordinateContext
{
    public PathCoordinateContext(IEnumerable<PathPixelRectangle> monitors, PathPosition origin,
        PathPixelRectangle? client = null)
    {
        Monitors = Array.AsReadOnly(monitors.ToArray());
        Origin = origin;
        Client = client;
    }

    public IReadOnlyList<PathPixelRectangle> Monitors { get; }
    public PathPosition Origin { get; }
    public PathPixelRectangle? Client { get; }
    public bool IsValid => Monitors.Count > 0 && Monitors.All(r => r.IsValid) && PathGeometry.IsFinite(Origin);

    public bool ContainsPosition(PathPosition point) => IsValid && PathGeometry.IsFinite(point) &&
        Monitors.Any(r => r.Contains(MousePathCoordinateResolver.Quantize(point)));

    public bool ContainsSegment(PathPosition first, PathPosition last)
    {
        if (!ContainsPosition(first) || !ContainsPosition(last))
            return false;
        var intervals = new List<(double Start, double End)>();
        foreach (var monitor in Monitors)
        {
            var start = 0d;
            var end = 1d;
            // 用像素单元覆盖量化前的位置，紧邻显示器之间不会产生虚构的一像素空隙。
            if (Clip(first.X, last.X - first.X, monitor.Left - .5, monitor.Right + .5, ref start, ref end) &&
                Clip(first.Y, last.Y - first.Y, monitor.Top - .5, monitor.Bottom + .5, ref start, ref end))
                intervals.Add((start, end));
        }
        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        var covered = 0d;
        foreach (var interval in intervals)
        {
            if (interval.Start > covered)
                return false;
            covered = Math.Max(covered, interval.End);
            if (covered >= 1)
                return true;
        }
        return false;
    }

    private static bool Clip(double origin, double delta, double minimum, double maximum,
        ref double start, ref double end)
    {
        if (delta == 0)
            return origin >= minimum && origin <= maximum;
        var a = (minimum - origin) / delta;
        var b = (maximum - origin) / delta;
        start = Math.Max(start, Math.Min(a, b));
        end = Math.Min(end, Math.Max(a, b));
        return start <= end;
    }
}
