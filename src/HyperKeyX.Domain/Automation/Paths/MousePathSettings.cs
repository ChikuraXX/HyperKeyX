namespace HyperKeyX.Domain.Automation.Paths;

public sealed record MousePathTimingSettings
{
    public MousePathTimingMode Mode { get; set; } = MousePathTimingMode.Original;
    public long FixedDurationUs { get; set; } = 1_000_000;
    public double SpeedPxPerSecond { get; set; } = 1_000;
    public double MaxSpeedPxPerSecond { get; set; } = 10_000;
    public double MaxAccelerationPxPerSecondSquared { get; set; } = 100_000;
}

public sealed record MousePathProcessingSettings
{
    public long SamplingWindowUs { get; set; } = 8_000;
    public double MovementThresholdPx { get; set; } = 2;
    public long StillnessDurationUs { get; set; } = 120_000;
    public bool SmoothingEnabled { get; set; } = true;
    public double SmoothingMaxDeviationPx { get; set; } = 2;
    public bool SimplificationEnabled { get; set; } = true;
    public double SimplificationMaxDeviationPx { get; set; } = 1;
    public long SimplificationMaxTimeErrorUs { get; set; } = 2_000;
}

public sealed record MousePathRandomizationSettings
{
    public bool TranslationEnabled { get; set; }
    public double TranslationXPx { get; set; } = 3;
    public double TranslationYPx { get; set; } = 3;
    public bool JitterEnabled { get; set; }
    public double JitterAmplitudePx { get; set; } = 1;
    public double JitterCorrelationDistancePx { get; set; } = 64;
}

/// <summary>录制时的物理客户区或桌面矩形，归一化点可据此还原源物理距离。</summary>
public sealed record MousePathRecordedFrame
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
