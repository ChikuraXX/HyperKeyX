using System.Threading.Channels;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public readonly record struct MousePathCaptureCutoff(long Sequence, long TimestampUs, bool Overflowed);

public sealed class MousePathCaptureBuffer
{
    private readonly object _gate = new();
    private readonly Channel<CapturedMacroEvent> _channel;
    private long _sequence;
    private long _lastTime;
    private MousePathCaptureCutoff? _cutoff;

    public MousePathCaptureBuffer(int capacity = MousePathLimits.CaptureQueueCapacity)
    {
        if (capacity is <= 0 or > MousePathLimits.CaptureQueueCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _channel = Channel.CreateBounded<CapturedMacroEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryAppend(CapturedMacroEvent input)
    {
        lock (_gate)
        {
            if (_cutoff is not null) return false;
            input = input with { Sequence = _sequence + 1, Context = input.Context?.DeepClone() };
            if (!_channel.Writer.TryWrite(input))
            {
                _cutoff = new(_sequence, _lastTime, true);
                _channel.Writer.TryComplete();
                return false;
            }
            _sequence++;
            _lastTime = Math.Max(_lastTime, input.TimestampUs);
            return true;
        }
    }

    public MousePathCaptureCutoff Close(long timestampUs)
    {
        lock (_gate)
        {
            // 截止与入队共用边界，完成后重复停止不会扩大截止点。
            _cutoff ??= new(_sequence, Math.Max(timestampUs, _lastTime), false);
            _channel.Writer.TryComplete();
            return _cutoff.Value;
        }
    }

    public IAsyncEnumerable<CapturedMacroEvent> ReadAllAsync(CancellationToken token) => _channel.Reader.ReadAllAsync(token);
}
