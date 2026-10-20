using System.Diagnostics;

namespace HyperKeyX.Application.Automation.Recording;

internal sealed class StopwatchMacroRecordingClock : IMacroRecordingClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long start, long end) =>
        Stopwatch.GetElapsedTime(start, end);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
