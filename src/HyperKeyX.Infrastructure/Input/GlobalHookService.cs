using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation;
using static HyperKeyX.Infrastructure.Interop.InputNativeMethods;
using static HyperKeyX.Infrastructure.Interop.KeyboardNativeMethods;

namespace HyperKeyX.Infrastructure.Input;

/// <summary>
/// 在专用消息线程上运行 WH_KEYBOARD_LL，并过滤本程序注入的输入。
/// </summary>
public sealed class GlobalHookService : IGlobalHookService, IKeyboardStateSource, IKeyboardHookLifetimeControl, IDisposable
{
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _startupSignal = new(false);
    private readonly Dictionary<int, KeyboardKeyIdentity> _pressedKeys = new();
    // 记录真正返回给 Win32 的决定，订阅者异常也不能破坏已放行/拦截的按键配对。
    private readonly Dictionary<int, bool> _pressDispositions = new();
    private readonly Func<int, short> _readKeyState;
    private readonly Func<ThreadStart, Thread> _createThread;
    private readonly Action<Thread> _startThread;
    private TaskCompletionSource? _lifetime;

    /// <inheritdoc />
    public Task CurrentLifetime
    {
        get { lock (_sync) return _lifetime?.Task ?? Task.CompletedTask; }
    }

    /// <summary>使用当前 Windows 按键高位建立启动快照。</summary>
    public GlobalHookService() : this(GetAsyncKeyState) { }

    /// <summary>隔离系统状态读取，便于使用可控的原生适配器验证启动边界。</summary>
    internal GlobalHookService(Func<int, short> readKeyState)
        : this(readKeyState, CreateHookThread, thread => thread.Start()) { }

    /// <summary>分别隔离线程创建与启动；创建返回未启动线程，启动操作仅在未启动成功时抛错。</summary>
    internal GlobalHookService(Func<int, short> readKeyState,
        Func<ThreadStart, Thread> createThread, Action<Thread> startThread)
    {
        _readKeyState = readKeyState;
        _createThread = createThread;
        _startThread = startThread;
    }

    /// <summary>创建本服务专用后台线程，尚不调度原生钩子入口。</summary>
    private static Thread CreateHookThread(ThreadStart entry) => new(entry)
    {
        Name = "HyperKeyX.GlobalHook",
        IsBackground = true
    };

    /// <inheritdoc />
    public IReadOnlyCollection<KeyboardKeyIdentity> GetPressedKeys()
    {
        lock (_sync) return _pressedKeys.Values.ToArray();
    }

    /// <summary>安装钩子后、消息泵开始前取得基线；左右修饰键分别读取，忽略切换低位。</summary>
    internal void InitializePressedKeys()
    {
        lock (_sync)
        {
            _pressedKeys.Clear();
            _pressDispositions.Clear();
            for (var key = 8; key <= 0xFE; key++)
            {
                if (key is 0x10 or 0x11 or 0x12 || (_readKeyState(key) & 0x8000) == 0) continue;
                _pressedKeys[key] = KeyboardKeyIdentity.FromStorageKey(key);
                _pressDispositions[key] = false;
                // VK_RETURN 无法区分安装前的两个回车，均作为被动按住状态，不启动动作。
                if (key == 0x0D)
                {
                    _pressedKeys[KeyboardKeyIdentity.NumpadEnterStorageKey] =
                        new(KeyboardKeyIdentity.NumpadEnterStorageKey, 0x0D, 0, true);
                    _pressDispositions[KeyboardKeyIdentity.NumpadEnterStorageKey] = false;
                }
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private nint _hookId;
    private LowLevelKeyboardProc? _hookProc;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Exception? _startupException;
    private bool _stopRequested;
    private bool _disposed;

    public event EventHandler<KeyboardInputEventArgs>? AnyKeyStateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _hookId != nint.Zero;
        }
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_hookThread is { IsAlive: true })
                return;

            _startupException = null;
            _stopRequested = false;
            _startupSignal.Reset();
            // 每次安装有独立身份；旧线程最终只能完成自己的任务。
            var lifetime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _lifetime = lifetime;
            try
            {
                _hookThread = _createThread(() => HookThreadProc(lifetime));
                _startThread(_hookThread);
            }
            catch (Exception exception)
            {
                // 入口尚未执行，其 finally 无法收尾；只清理本次未启动成功的安装。
                if (ReferenceEquals(_lifetime, lifetime))
                {
                    _hookThread = null;
                    _hookThreadId = 0;
                    _hookId = nint.Zero;
                    _hookProc = null;
                    _pressedKeys.Clear();
                    _pressDispositions.Clear();
                    _stopRequested = true;
                    _startupException = exception;
                    _startupSignal.Set();
                }
                // 此任务表示安装已结束，不表示启动成功；启动错误由原调用栈传播。
                lifetime.TrySetResult();
                throw;
            }
        }

        // Start 只有在钩子真正安装成功后才返回，避免界面出现假运行状态。
        if (!_startupSignal.Wait(TimeSpan.FromSeconds(3)))
        {
            Stop();
            throw new TimeoutException("全局键盘钩子启动超时。");
        }

        Exception? startupException;
        lock (_sync)
            startupException = _startupException;

        if (startupException != null)
            throw new InvalidOperationException("无法启动全局键盘钩子。", startupException);
    }

    public void Stop() => WaitForHookThread(RequestStop(expectedLifetime: null));

    /// <inheritdoc />
    public bool StopIfCurrent(Task lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var thread = RequestStop(lifetime);
        WaitForHookThread(thread);
        return thread is not null;
    }

    /// <summary>核对安装身份并在同一锁内投递退出，防止旧线程 ID 被新安装复用。</summary>
    private Thread? RequestStop(Task? expectedLifetime)
    {
        lock (_sync)
        {
            if (expectedLifetime is not null && !ReferenceEquals(_lifetime?.Task, expectedLifetime)) return null;
            var thread = _hookThread;
            if (thread is null) return null;
            _stopRequested = true;
            if (_hookThreadId != 0 && !PostThreadMessage(_hookThreadId, WM_QUIT, 0, nint.Zero))
                Debug.WriteLine($"[GlobalHookService] 发送退出消息失败: {Marshal.GetLastWin32Error()}");
            return thread;
        }
    }

    /// <summary>等待捕获的原线程，等待期间不占用状态锁，也不重新查找可能已经替换的线程。</summary>
    private static void WaitForHookThread(Thread? thread)
    {
        if (thread is not null && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(3)))
            Debug.WriteLine("[GlobalHookService] 钩子线程未在限定时间内退出。");
    }

    private void HookThreadProc(TaskCompletionSource lifetime)
    {
        var startupSignaled = false;

        try
        {
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;

            lock (_sync)
                _hookThreadId = GetCurrentThreadId();

            // 先创建线程消息队列，确保紧接着到来的 Stop 能可靠投递 WM_QUIT。
            _ = PeekMessage(out _, nint.Zero, 0, 0, PM_NOREMOVE);

            _hookProc = HookCallback;
            var hookId = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _hookProc,
                GetModuleHandle(module?.ModuleName ?? string.Empty),
                0);

            if (hookId == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx 调用失败。");

            lock (_sync)
                _hookId = hookId;

            InitializePressedKeys();
            _startupSignal.Set();
            startupSignaled = true;

            lock (_sync)
            {
                if (_stopRequested)
                    return;
            }

            // 低级键盘钩子的回调由消息泵分发，Thread.Sleep 无法替代此循环。
            while (true)
            {
                var result = GetMessage(out var message, nint.Zero, 0, 0);
                if (result == 0)
                    break;
                if (result == -1)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMessage 调用失败。");

                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalHookService] {ex}");
            if (!startupSignaled)
            {
                lock (_sync)
                    _startupException = ex;
            }
        }
        finally
        {
            nint hookId;
            lock (_sync)
                hookId = _hookId;

            if (hookId != nint.Zero)
                _ = UnhookWindowsHookEx(hookId);

            lock (_sync)
            {
                _pressedKeys.Clear();
                _pressDispositions.Clear();
                _hookId = nint.Zero;
                _hookThreadId = 0;
                // 在允许新安装前完成旧启动等待的唤醒，不能误唤醒新一轮 Start。
                _startupSignal.Set();
                _hookThread = null;
            }

            // 钩子已卸载且消息分发结束；异步续接不能打断原生资源清理。
            lifetime.TrySetResult();
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var hookData = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // 本程序注入的输入带有哨兵值，跳过后可阻止连发递归触发自身。
                if (hookData.dwExtraInfo != TurboExtraInfo)
                {
                    var isDown = wParam == (nint)WM_KEYDOWN || wParam == (nint)WM_SYSKEYDOWN;
                    var isUp = wParam == (nint)WM_KEYUP || wParam == (nint)WM_SYSKEYUP;
                    if (isDown || isUp)
                    {
                        var key = KeyboardKeyIdentity.FromHook(
                            hookData.vkCode,
                            hookData.scanCode,
                            hookData.flags);
                        bool isRepeat;
                        lock (_sync) isRepeat = isDown && _pressedKeys.ContainsKey(key.StorageKey);

                        var stamp = InputCaptureClock.Next();
                        var input = new KeyboardInputEvent(key, isDown, isRepeat, stamp.TimestampUs, stamp.Sequence,
                            (hookData.flags & 0x10) != 0);
                        if (DispatchInput(input))
                            return (nint)1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 异常绝不能越过 Win32 回调边界，否则可能破坏系统输入链。
            Debug.WriteLine($"[GlobalHookService] 钩子回调异常: {ex}");
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    internal bool DispatchInput(KeyboardInputEvent input)
    {
        lock (_sync)
        {
            if (input.IsDown) _pressedKeys[input.Key.StorageKey] = input.Key;
            else
            {
                _pressedKeys.Remove(input.Key.StorageKey);
                if (input.Key.VirtualKeyCode == 0x0D)
                {
                    // 清除无法区分的启动基线；后续真实按下仍由扩展位独立识别。
                    var other = input.Key.IsExtendedKey ? 0x0D : KeyboardKeyIdentity.NumpadEnterStorageKey;
                    if (_pressedKeys.TryGetValue(other, out var held) && held.ScanCode == 0)
                    {
                        _pressedKeys.Remove(other);
                        _pressDispositions.Remove(other);
                    }
                }
            }
        }
        var args = new KeyboardInputEventArgs(input);
        var suppress = false;
        try
        {
            AnyKeyStateChanged?.Invoke(this, args);
            suppress = args.SuppressOriginalInput;
        }
        catch (Exception ex)
        {
            // 新按下故障开放；已有按下的后续事件仍沿用真实返回值，避免吞掉必要抬起。
            Debug.WriteLine($"[GlobalHookService] 钩子订阅者异常: {ex}");
        }
        lock (_sync)
        {
            if (!input.IsDown)
                return _pressDispositions.Remove(input.Key.StorageKey, out var previous) && previous;
            if (_pressDispositions.TryGetValue(input.Key.StorageKey, out var existing)) return existing;
            _pressDispositions[input.Key.StorageKey] = suppress;
            return suppress;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        lock (_sync)
        {
            if (_hookThread == null)
                _startupSignal.Dispose();
        }
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GlobalHookService));
    }
}
