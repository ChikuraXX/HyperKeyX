using System.Diagnostics;

namespace HyperKeyX.Application.Automation.Recording;

/// <summary>记录当前线程的同步录制通知，避免重入释放等待自身；标记不流入后台清理任务。</summary>
internal static class RecordingNotificationContext
{
    /// <summary>当前线程的通知嵌套深度，最外层通知结束后恢复为零。</summary>
    [ThreadStatic]
    private static int _notificationDepth;

    /// <summary>进入录制通知作用域；返回值必须在同步通知结束时释放。</summary>
    internal static IDisposable Enter()
    {
        _notificationDepth++;
        return new NotificationScope();
    }

    /// <summary>通知外同步等待释放；通知内由原生命周期任务完成释放，并观察异步失败。</summary>
    internal static void WaitForDisposal(IDisposable owner, Task disposal)
    {
        // 服务与协调器可能互相依赖，任何录制通知中都不能同步等待录制释放。
        if (_notificationDepth > 0)
        {
            _ = ObserveDisposalAsync(owner, disposal);
            return;
        }
        disposal.GetAwaiter().GetResult();
    }

    /// <summary>重入释放无法向原调用者同步返回异常，在这一恢复边界记录完整原因。</summary>
    private static async Task ObserveDisposalAsync(IDisposable owner, Task disposal)
    {
        try { await disposal.ConfigureAwait(false); }
        catch (Exception exception) { Debug.WriteLine($"[{owner.GetType().Name}] 录制回调中的延后释放失败：{exception}"); }
    }

    /// <summary>同步通知计数作用域，由通知方法的 using 保证异常时也恢复深度。</summary>
    private sealed class NotificationScope : IDisposable
    {
        public void Dispose() => _notificationDepth--;
    }
}
