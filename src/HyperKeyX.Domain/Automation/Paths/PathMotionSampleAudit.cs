namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>检查名义发送序列或实际发送时间；只维护本次序列的量化位置与区间速度。</summary>
public sealed class PathMotionSampleAudit
{
    private readonly MousePathTimingSettings _settings;
    private TimedPathPosition? _previous;
    private PathPosition _velocity;
    private double _intervalSeconds;
    private PathIssue? _failure;

    public PathMotionSampleAudit(MousePathTimingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings with { };
        if (!double.IsFinite(settings.MaxSpeedPxPerSecond) || settings.MaxSpeedPxPerSecond <= 0 ||
            !double.IsFinite(settings.MaxAccelerationPxPerSecondSquared) || settings.MaxAccelerationPxPerSecondSquared <= 0)
            _failure = new(PathIssueCode.InvalidSettings);
    }

    public PathIssue? Check(TimedPathPosition sample)
    {
        var copy = new PathMotionSampleAudit(_settings)
        {
            _previous = _previous, _velocity = _velocity,
            _intervalSeconds = _intervalSeconds, _failure = _failure
        };
        return copy.Accept(sample);
    }

    public PathIssue? Accept(TimedPathPosition sample)
    {
        if (_failure is not null) return _failure;
        if (!PathGeometry.IsFinite(sample.Position) || sample.OffsetUs < 0)
            return _failure = new(PathIssueCode.InvalidPoint);
        sample = sample with { Position = MousePathCoordinateResolver.Quantize(sample.Position) };
        if (_previous is not { } previous)
        {
            _previous = sample;
            return null;
        }
        if (sample.OffsetUs < previous.OffsetUs)
            return _failure = new(PathIssueCode.InvalidTimeOrder);
        if (sample.OffsetUs == previous.OffsetUs)
            return sample.Position == previous.Position ? null : _failure = new(PathIssueCode.MotionLimitExceeded);

        var seconds = (sample.OffsetUs - previous.OffsetUs) / 1_000_000d;
        var velocity = PathMotionSpan.Scale(PathMotionSpan.Subtract(sample.Position, previous.Position), 1 / seconds);
        // 区间速度位于区间中点；首段从静止开始，最后一段在 Complete 中验证停止。
        var acceleration = PathGeometry.Distance(_velocity, velocity) / ((_intervalSeconds + seconds) / 2);
        if (PathGeometry.Distance(default, velocity) > _settings.MaxSpeedPxPerSecond ||
            acceleration > _settings.MaxAccelerationPxPerSecondSquared)
            return _failure = new(PathIssueCode.MotionLimitExceeded);
        _previous = sample;
        _velocity = velocity;
        _intervalSeconds = seconds;
        return null;
    }

    public PathIssue? Complete()
    {
        if (_failure is not null) return _failure;
        if (_intervalSeconds > 0 && PathGeometry.Distance(default, _velocity) / (_intervalSeconds / 2) >
            _settings.MaxAccelerationPxPerSecondSquared)
            return _failure = new(PathIssueCode.MotionLimitExceeded);
        return null;
    }
}
