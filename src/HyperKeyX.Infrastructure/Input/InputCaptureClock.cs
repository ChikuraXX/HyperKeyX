using System.Diagnostics;

namespace HyperKeyX.Infrastructure.Input;

internal static class InputCaptureClock
{
    private static readonly object Gate = new();
    private static long _sequence;

    public static (long TimestampUs, long Sequence) Next()
    {
        // 键盘与鼠标钩子共享微秒时钟和全序，时间戳相同时仍可确定按键边界顺序。
        lock (Gate)
            return (Stopwatch.GetElapsedTime(0).Ticks / 10, ++_sequence);
    }
}
