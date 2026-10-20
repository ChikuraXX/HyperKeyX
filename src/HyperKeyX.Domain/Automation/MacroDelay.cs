namespace HyperKeyX.Domain.Automation;

/// <summary>解析步骤本次执行的延时；固定延时不消耗随机数，随机模式每次执行重新采样。</summary>
public static class MacroDelay
{
    public static bool HasValidRange(MacroStep step) => Enum.IsDefined(step.DelayDistribution) &&
        (step.RandomDurationMaxMs is not { } maximum ||
        (step.ActionType != MacroActionType.MousePath && step.DurationMs >= 0 &&
         maximum >= step.DurationMs && maximum <= MacroSequenceValidator.MaximumDurationMs));

    /// <summary>随机源使用下限含、上限不含的约定，返回值包含配置的两个端点。</summary>
    public static int ResolveMilliseconds(MacroStep step, Func<int, int, int>? next = null,
        Func<double>? nextUnit = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.RandomDurationMaxMs is not { } maximum)
            return Math.Clamp(step.DurationMs, 0, MacroSequenceValidator.MaximumDurationMs);
        if (!HasValidRange(step))
            throw new ArgumentOutOfRangeException(nameof(step), "随机延时必须满足 0 ≤ 下限 ≤ 上限 ≤ 60000，且不能用于轨迹动作。");
        if (step.DurationMs == maximum)
            return maximum;
        if (step.DelayDistribution == MacroDelayDistribution.Normal)
            return SampleTruncatedNormal(step.DurationMs, maximum, nextUnit is null ? Random.Shared.NextDouble() : nextUnit());
        return next is null ? Random.Shared.Next(step.DurationMs, maximum + 1) : next(step.DurationMs, maximum + 1);
    }

    /// <summary>对 ±3σ 内的正态 CDF 做有界反演，避免拒绝采样的无限重试与端点裁剪堆积。</summary>
    private static int SampleTruncatedNormal(int minimum, int maximum, double unit)
    {
        if (!(unit >= 0 && unit < 1))
            throw new ArgumentOutOfRangeException(nameof(unit), "随机源必须返回 [0, 1)。");
        var lowerProbability = NormalCdf(-3);
        var probability = lowerProbability + unit * (NormalCdf(3) - lowerProbability);
        double lower = -3, upper = 3;
        // 固定次数反演；最大 60000ms 区间下二分误差小于 0.001ms。
        for (var i = 0; i < 28; i++)
        {
            var middle = (lower + upper) / 2;
            if (NormalCdf(middle) < probability) lower = middle;
            else upper = middle;
        }
        var mean = (minimum + maximum) / 2.0;
        var sigma = (maximum - minimum) / 6.0;
        var milliseconds = (int)Math.Round(mean + sigma * (lower + upper) / 2,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(milliseconds, minimum, maximum);
    }

    /// <summary>标准正态 CDF 的五项近似（绝对误差约 7.5e-8），足以用于整数毫秒取值。</summary>
    private static double NormalCdf(double value)
    {
        var x = Math.Abs(value);
        var t = 1 / (1 + 0.2316419 * x);
        var polynomial = t * (0.319381530 + t * (-0.356563782 +
            t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        var tail = 0.3989422804014327 * Math.Exp(-x * x / 2) * polynomial;
        return value >= 0 ? 1 - tail : tail;
    }
}
