using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static HyperKeyX.Infrastructure.Interop.ProcessNativeMethods;

namespace HyperKeyX.Infrastructure.Windows;

/// <summary>专用消息线程接收进程外窗口生命周期事件，不向目标进程注入钩子 DLL。</summary>
internal sealed class WindowLifetimeHub : IDisposable
{
    private const uint DrainMessage = 0x8000 + 73;
    private readonly object _gate = new();
    private readonly WindowLifetimeRegistry _registry = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<nuint, TaskCompletionSource<bool>> _pending = new();
    private Thread? _thread;
    private uint _threadId;
    private long _nextDrain;
    private volatile bool _stopping;
    private volatile bool _running;

    public IDisposable? Watch(nint handle, Action invalidated)
    {
        lock (_gate)
        {
            if (_stopping) return null;
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "HyperKeyX.WindowLifetime" };
                _thread.Start();
            }
        }
        return _ready.Task.Wait(TimeSpan.FromSeconds(3)) && _ready.Task.Result && !_stopping
            ? _registry.Watch(handle, invalidated) : null;
    }

    public bool Flush()
    {
        if (!_running || _stopping) return false;
        if (GetCurrentThreadId() == Volatile.Read(ref _threadId)) return true;
        var id = (nuint)Interlocked.Increment(ref _nextDrain);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.TryAdd(id, completion);
        try
        {
            // 先处理已排队的生命周期通知，再确认实例。超时即失效，不沿用缓存句柄。
            return PostThreadMessage(Volatile.Read(ref _threadId), DrainMessage, id, 0) &&
                completion.Task.Wait(TimeSpan.FromMilliseconds(50)) && completion.Task.Result;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private void Run()
    {
        nint hook = 0;
        GCHandle root = default;
        try
        {
            _ = PeekMessage(out _, 0, 0, 0, PM_NOREMOVE);
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            WinEventProc callback = (_, type, handle, objectId, childId, _, _) =>
                _registry.Notify(type, handle, objectId, childId);
            root = GCHandle.Alloc(callback);
            hook = SetWinEventHook(0x8000, 0x8001, 0, callback, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (hook == 0) throw new Win32Exception();
            _running = true;
            _ready.TrySetResult(true);
            while (!_stopping)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result <= 0) break;
                if (message.Id == DrainMessage && _pending.TryRemove(message.WParam, out var completion))
                    completion.TrySetResult(true);
            }
        }
        catch (Exception) { _ready.TrySetResult(false); }
        finally
        {
            _running = false;
            _registry.FailAll();
            foreach (var completion in _pending.Values) completion.TrySetResult(false);
            _pending.Clear();
            if (hook != 0) _ = UnhookWinEvent(hook);
            if (root.IsAllocated) root.Free();
            _ready.TrySetResult(false);
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            thread = _thread;
        }
        _registry.FailAll();
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0) _ = PostThreadMessage(threadId, WM_QUIT, 0, 0);
        if (thread is not null && Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(3)))
            throw new TimeoutException("停止窗口生命周期监视线程超时。");
    }
}
