namespace HyperKeyX.Application.Automation.Recording;

internal interface IMacroRecordingClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long start, long end);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
