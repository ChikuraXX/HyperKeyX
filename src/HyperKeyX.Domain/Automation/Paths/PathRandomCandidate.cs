namespace HyperKeyX.Domain.Automation.Paths;

internal readonly record struct PathRandomCandidate(PathPosition Translation, ulong NoiseSeed)
{
    public static PathRandomCandidate Create(MousePathRandomizationSettings settings, IPathRandomSource source)
    {
        var translation = settings.TranslationEnabled
            ? new PathPosition((Unit(source) * 2 - 1) * settings.TranslationXPx,
                (Unit(source) * 2 - 1) * settings.TranslationYPx) : default;
        var seed = settings.JitterEnabled ? (ulong)(Unit(source) * 9_007_199_254_740_992d) : 0;
        return new(translation, seed);
    }

    public PathPosition Offset(double distance, double sinceAnchor, double untilAnchor,
        MousePathRandomizationSettings settings)
    {
        if (!settings.JitterEnabled || sinceAnchor <= 0 || untilAnchor <= 0)
            return default;
        var progress = distance / settings.JitterCorrelationDistancePx;
        var index = (ulong)Math.Floor(progress);
        var value = PathGeometry.Lerp(Node(index), Node(index + 1), Ease(progress - Math.Floor(progress)));
        var taperLength = settings.JitterCorrelationDistancePx / 4;
        var taper = Ease(Math.Min(1, sinceAnchor / taperLength)) * Ease(Math.Min(1, untilAnchor / taperLength));
        return PathMotionSpan.Scale(value, settings.JitterAmplitudePx * taper);
    }

    private PathPosition Node(ulong index)
    {
        var angle = Hash(index * 2) * 2 * Math.PI;
        var radius = Math.Sqrt(Hash(index * 2 + 1));
        return new(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
    }

    private double Hash(ulong index)
    {
        // 按有序弧长索引取值；播放查询次数和线程调度不会改变随机场。
        unchecked
        {
            var value = NoiseSeed + 0x9E3779B97F4A7C15UL * (index + 1);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return ((value ^ (value >> 31)) >> 11) / 9_007_199_254_740_992d;
        }
    }

    private static double Ease(double value) => value * value * (3 - 2 * value);
    private static double Unit(IPathRandomSource source)
    {
        var value = source.NextUnit();
        if (!double.IsFinite(value) || value is < 0 or >= 1)
            throw new InvalidOperationException("轨迹随机源必须返回 [0, 1)。");
        return value;
    }
}
