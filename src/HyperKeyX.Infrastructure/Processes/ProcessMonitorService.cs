using System.ComponentModel;
using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Processes;
using static HyperKeyX.Infrastructure.Interop.ProcessNativeMethods;

namespace HyperKeyX.Infrastructure.Processes;

/// <summary>
/// 通过系统前台窗口事件发布进程变化，不使用定时轮询。
/// </summary>
public sealed class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    private static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private Thread? _messageThread;
    private ManualResetEventSlim? _startupSignal;
    private Exception? _startupFailure;
    private nint _eventHook;
    private uint _messageThreadId;
    private string _lastProcessName = string.Empty;
    private bool _disposed;

    public event EventHandler<string>? ForegroundProcessChanged;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _eventHook != nint.Zero && _messageThread?.IsAlive == true;
        }
    }

    public void Start()
    {
        Thread thread;
        ManualResetEventSlim startupSignal;

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_messageThread?.IsAlive == true)
                return;

            _startupSignal?.Dispose();
            startupSignal = new ManualResetEventSlim(false);
            _startupSignal = startupSignal;
            _startupFailure = null;
            _lastProcessName = string.Empty;
            thread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "HyperKeyX.ProcessMonitor"
            };
            _messageThread = thread;
            thread.Start();
        }

        if (!startupSignal.Wait(ThreadTimeout))
        {
            Stop();
            throw new TimeoutException("启动前台进程监视线程超时。");
        }

        Exception? startupFailure;
        lock (_sync)
            startupFailure = _startupFailure;

        if (startupFailure != null)
        {
            Stop();
            throw new InvalidOperationException("无法启动前台进程监视。", startupFailure);
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_sync)
        {
            thread = _messageThread;
            threadId = _messageThreadId;
        }

        if (thread == null)
            return;

        if (threadId != 0 && !PostThreadMessage(threadId, WM_QUIT, 0, nint.Zero))
        {
            Debug.WriteLine(
                $"[ProcessMonitorService] 无法通知监视线程退出: {new Win32Exception()}");
        }

        if (Thread.CurrentThread != thread && !thread.Join(ThreadTimeout))
            Debug.WriteLine("[ProcessMonitorService] 等待监视线程退出超时。");

        lock (_sync)
        {
            if (_messageThread == thread && !thread.IsAlive)
                _messageThread = null;
        }
    }

    public bool TryGetForegroundProcessName(out string processName)
    {
        processName = string.Empty;

        try
        {
            var window = GetForegroundWindow();
            if (window == nint.Zero)
                return false;

            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
                return false;

            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName.ToLowerInvariant() + ".exe";
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessMonitorService] 读取前台进程失败: {ex}");
            return false;
        }
    }

    private void MessageThreadMain()
    {
        nint hook = nint.Zero;

        try
        {
            // PostThreadMessage 只有在线程创建消息队列后才可靠。
            _ = PeekMessage(out _, nint.Zero, 0, 0, PM_NOREMOVE);
            var threadId = GetCurrentThreadId();
            WinEventProc callback = OnWinEvent;
            hook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                nint.Zero,
                callback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);
            if (hook == nint.Zero)
                throw new Win32Exception();

            lock (_sync)
            {
                _messageThreadId = threadId;
                _eventHook = hook;
            }

            _startupSignal?.Set();
            PublishCurrentForegroundProcess();

            while (true)
            {
                var result = GetMessage(out _, nint.Zero, 0, 0);
                if (result > 0)
                    continue;
                if (result == 0)
                    break;

                throw new Win32Exception();
            }

            GC.KeepAlive(callback);
        }
        catch (Exception ex)
        {
            lock (_sync)
                _startupFailure ??= ex;

            _startupSignal?.Set();
            PublishForegroundProcess(string.Empty);
            Debug.WriteLine($"[ProcessMonitorService] 监视线程失败: {ex}");
        }
        finally
        {
            if (hook != nint.Zero && !UnhookWinEvent(hook))
            {
                Debug.WriteLine(
                    $"[ProcessMonitorService] 释放前台窗口钩子失败: {new Win32Exception()}");
            }

            lock (_sync)
            {
                _eventHook = nint.Zero;
                _messageThreadId = 0;
                _lastProcessName = string.Empty;
            }

            _startupSignal?.Set();
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND)
            PublishCurrentForegroundProcess();
    }

    private void PublishCurrentForegroundProcess()
    {
        if (TryGetForegroundProcessName(out var processName))
        {
            PublishForegroundProcess(processName);
            return;
        }

        PublishForegroundProcess(string.Empty);
    }

    private void PublishForegroundProcess(string processName)
    {
        EventHandler<string>? handler;

        lock (_sync)
        {
            if (string.Equals(
                    processName,
                    _lastProcessName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastProcessName = processName;
            handler = ForegroundProcessChanged;
        }

        handler?.Invoke(this, processName);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
        }

        Stop();

        lock (_sync)
        {
            _startupSignal?.Dispose();
            _startupSignal = null;
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessMonitorService));
    }
}
