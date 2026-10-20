using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Input;

namespace HyperKeyX.Infrastructure.Input;

public sealed class MonotonicMousePathClock : IMousePathClock
{
    public long NowUs => Stopwatch.GetElapsedTime(0).Ticks / 10;
    public async Task DelayUntilAsync(long deadlineUs, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadlineUs - NowUs;
            if (remaining <= 0) return;
            // 使用绝对期限重查余量，避免相对等待逐点累计漂移或忙循环。
            await Task.Delay(TimeSpan.FromTicks(Math.Min(remaining, 25_000) * 10), cancellationToken).ConfigureAwait(false);
        }
    }
}
