namespace HyperKeyX.Domain.Automation.Paths;

public static class PathGeometry
{
    public static double Distance(PathPosition first, PathPosition second)
    {
        var x = Math.Abs(second.X - first.X);
        var y = Math.Abs(second.Y - first.Y);
        var scale = Math.Max(x, y);
        if (scale == 0 || !double.IsFinite(scale))
            return scale;
        return scale * Math.Sqrt((x / scale) * (x / scale) + (y / scale) * (y / scale));
    }

    public static PathPosition Lerp(PathPosition first, PathPosition second, double progress) =>
        new(first.X * (1 - progress) + second.X * progress,
            first.Y * (1 - progress) + second.Y * progress);

    public static bool IsFinite(PathPosition point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
