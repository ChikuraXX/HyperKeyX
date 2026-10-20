using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using static HyperKeyX.Infrastructure.Interop.InputNativeMethods;
using static HyperKeyX.Infrastructure.Interop.MouseNativeMethods;

namespace HyperKeyX.Infrastructure.Input;

/// <summary>
/// 在独立消息线程上运行 WH_MOUSE_LL，并同步发布可阻止的鼠标事件。
/// </summary>
public sealed class GlobalMouseHookService : IGlobalMouseHookService, IPointerCaptureSource, IDisposable
{
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _startupSignal = new(false);

    private nint _hookId;
    private LowLevelMouseProc? _hookProc;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Exception? _startupException;
    private bool _stopRequested;
    private bool _disposed;
    private Action<MouseInputEvent>? _movementHandlers;

    public event EventHandler<MouseInputEventArgs>? AnyMouseInput;

    public bool IsMovementCaptureEnabled => Volatile.Read(ref _movementHandlers) is not null;

    public IDisposable SubscribeMovement(Action<MouseInputEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync)
        {
            ThrowIfDisposed();
            _movementHandlers += callback;
            return new MovementSubscription(this, callback);
        }
    }

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
            _hookThread = new Thread(HookThreadProc)
            {
                Name = "HyperKeyX.GlobalMouseHook",
                IsBackground = true
            };
            _hookThread.Start();
        }

        if (!_startupSignal.Wait(TimeSpan.FromSeconds(3)))
        {
            Stop();
            throw new TimeoutException("全局鼠标钩子启动超时。");
        }

        Exception? startupException;
        lock (_sync)
            startupException = _startupException;

        if (startupException is not null)
            throw new InvalidOperationException("无法启动全局鼠标钩子。", startupException);
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_sync)
        {
            thread = _hookThread;
            threadId = _hookThreadId;
            _stopRequested = true;
        }

        if (thread is null)
            return;

        if (threadId != 0 && !PostThreadMessage(threadId, WM_QUIT, 0, nint.Zero))
        {
            Debug.WriteLine(
                $"[GlobalMouseHookService] 发送退出消息失败: {Marshal.GetLastWin32Error()}");
        }

        if (thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(3)))
            Debug.WriteLine("[GlobalMouseHookService] 钩子线程未在限定时间内退出。");
    }

    internal static bool TryMapMessage(
        int message,
        uint mouseData,
        int targetProcessId,
        nint extraInfo,
        out MouseInputEvent input,
        PathPosition? position = null,
        long timestampUs = 0,
        long sequence = 0,
        bool includeMovement = false,
        uint flags = 0)
    {
        input = default;
        if (extraInfo == TurboExtraInfo)
            return false;

        var kind = MouseInputKind.ButtonDown;
        var button = MouseButtonKind.None;
        var wheelDelta = 0;

        switch (message)
        {
            case WM_MOUSEMOVE when includeMovement:
                kind = MouseInputKind.Move;
                break;
            case WM_LBUTTONDOWN:
                button = MouseButtonKind.Left;
                break;
            case WM_LBUTTONUP:
                kind = MouseInputKind.ButtonUp;
                button = MouseButtonKind.Left;
                break;
            case WM_RBUTTONDOWN:
                button = MouseButtonKind.Right;
                break;
            case WM_RBUTTONUP:
                kind = MouseInputKind.ButtonUp;
                button = MouseButtonKind.Right;
                break;
            case WM_MBUTTONDOWN:
                button = MouseButtonKind.Middle;
                break;
            case WM_MBUTTONUP:
                kind = MouseInputKind.ButtonUp;
                button = MouseButtonKind.Middle;
                break;
            case WM_XBUTTONDOWN:
                button = ReadXButton(mouseData);
                break;
            case WM_XBUTTONUP:
                kind = MouseInputKind.ButtonUp;
                button = ReadXButton(mouseData);
                break;
            case WM_MOUSEWHEEL:
                kind = MouseInputKind.Wheel;
                wheelDelta = unchecked((short)(mouseData >> 16));
                if (wheelDelta == 0)
                    return false;
                break;
            default:
                return false;
        }

        if (kind is not (MouseInputKind.Wheel or MouseInputKind.Move) && button == MouseButtonKind.None)
            return false;

        input = new MouseInputEvent(kind, button, wheelDelta, targetProcessId, position,
            timestampUs, sequence, (flags & 1) != 0);
        return true;
    }

    internal bool DispatchInput(MouseInputEvent input)
    {
        if (input.Kind == MouseInputKind.Move)
        {
            var handlers = Volatile.Read(ref _movementHandlers);
            if (handlers is not null)
            {
                foreach (Action<MouseInputEvent> handler in handlers.GetInvocationList())
                {
                    try { handler(input); }
                    catch (Exception exception) { Debug.WriteLine($"鼠标移动订阅者失败：{exception}"); }
                }
            }
            // 高频移动只交给显式订阅者，不进入旧触发器的阻止输入通路。
            return false;
        }
        var args = new MouseInputEventArgs(input);
        try
        {
            AnyMouseInput?.Invoke(this, args);
            return args.SuppressOriginalInput;
        }
        catch (Exception ex)
        {
            // 订阅者失败时继续钩子链，不能让应用异常影响系统鼠标。
            Debug.WriteLine($"[GlobalMouseHookService] 钩子订阅者异常: {ex}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        lock (_sync)
        {
            _movementHandlers = null;
            if (_hookThread is null)
                _startupSignal.Dispose();
        }

        _disposed = true;
    }

    private void HookThreadProc()
    {
        var startupSignaled = false;

        try
        {
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;

            lock (_sync)
                _hookThreadId = GetCurrentThreadId();

            // 先创建消息队列，确保紧接着发生的 Stop 能投递 WM_QUIT。
            _ = PeekMessage(out _, nint.Zero, 0, 0, PM_NOREMOVE);

            _hookProc = HookCallback;
            var hookId = SetWindowsHookEx(
                WH_MOUSE_LL,
                _hookProc,
                GetModuleHandle(module?.ModuleName ?? string.Empty),
                0);

            if (hookId == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "SetWindowsHookEx 调用失败。");
            }

            lock (_sync)
                _hookId = hookId;

            _startupSignal.Set();
            startupSignaled = true;

            lock (_sync)
            {
                if (_stopRequested)
                    return;
            }

            while (true)
            {
                var result = GetMessage(out var message, nint.Zero, 0, 0);
                if (result == 0)
                    break;
                if (result == -1)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "GetMessage 调用失败。");
                }

                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GlobalMouseHookService] {ex}");
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
                _hookId = nint.Zero;
                _hookThreadId = 0;
                _hookThread = null;
            }

            _startupSignal.Set();
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var message = unchecked((int)wParam);
                var captureMovement = IsMovementCaptureEnabled;
                if (message == WM_MOUSEMOVE && !captureMovement)
                    return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
                var hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var stamp = InputCaptureClock.Next();
                if (TryMapMessage(
                        message,
                        hookData.mouseData,
                        targetProcessId: 0,
                        hookData.dwExtraInfo,
                        out var input,
                        new(hookData.pt.x, hookData.pt.y), stamp.TimestampUs, stamp.Sequence,
                        captureMovement, hookData.flags))
                {
                    if (input.Kind != MouseInputKind.Move)
                        input = input with { TargetProcessId = ResolveTargetProcessId(hookData.pt) };
                    if (DispatchInput(input))
                        return (nint)1;
                }
            }
        }
        catch (Exception ex)
        {
            // Win32 回调边界始终故障开放，避免中断系统鼠标输入链。
            Debug.WriteLine($"[GlobalMouseHookService] 钩子回调异常: {ex}");
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private static MouseButtonKind ReadXButton(uint mouseData)
    {
        return (ushort)(mouseData >> 16) switch
        {
            1 => MouseButtonKind.XButton1,
            2 => MouseButtonKind.XButton2,
            _ => MouseButtonKind.None
        };
    }

    private static int ResolveTargetProcessId(POINT point)
    {
        var window = WindowFromPoint(point);
        if (window == nint.Zero)
            return 0;

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId <= int.MaxValue ? (int)processId : 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GlobalMouseHookService));
    }

    private sealed class MovementSubscription(GlobalMouseHookService owner, Action<MouseInputEvent> callback) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) owner._movementHandlers -= callback;
        }
    }
}
