using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class GlobalShortcutService : IGlobalShortcutService
{
    private const uint HotKeyMessage = 0x0312;
    private const int FirstShortcutId = 0x4858;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly object _gate = new();
    private readonly IGlobalShortcutNativeApi _native;
    private readonly Dictionary<int, GlobalShortcutAction> _actions = [];
    private IntPtr _windowHandle;
    private bool _isDisposed;

    public GlobalShortcutService()
        : this(new GlobalShortcutNativeApi())
    {
    }

    internal GlobalShortcutService(IGlobalShortcutNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public event EventHandler<GlobalShortcutAction>? ShortcutInvoked;

    public bool IsRegistered
    {
        get
        {
            lock (_gate)
                return _actions.Count > 0;
        }
    }

    public OperationResult Register(
        IntPtr windowHandle,
        IReadOnlyCollection<GlobalShortcutDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("Global shortcut service is disposed.");
            if (_actions.Count > 0)
                return OperationResult.Failure("Global shortcuts are already registered.");
            if (windowHandle == IntPtr.Zero)
                return OperationResult.Failure("Window handle is invalid.");
            if (definitions.Count == 0)
                return OperationResult.Failure("At least one global shortcut is required.");

            var registrations = PrepareRegistrations(definitions);
            if (!registrations.Succeeded)
                return OperationResult.Failure(registrations.ErrorMessage!);

            var registeredIds = new List<int>(registrations.Value!.Count);
            foreach (var registration in registrations.Value)
            {
                if (!_native.RegisterHotKey(
                        windowHandle,
                        registration.Id,
                        registration.NativeModifiers,
                        (uint)registration.Definition.VirtualKey))
                {
                    var error = _native.GetLastError();
                    RollBack(windowHandle, registeredIds);
                    return OperationResult.Failure(
                        $"Could not register global shortcut (Win32 error {error}).");
                }

                registeredIds.Add(registration.Id);
            }

            _windowHandle = windowHandle;
            foreach (var registration in registrations.Value)
                _actions.Add(registration.Id, registration.Definition.Action);
            return OperationResult.Success();
        }
    }

    public bool TryHandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != HotKeyMessage)
            return false;

        GlobalShortcutAction action;
        lock (_gate)
        {
            if (_isDisposed ||
                !_actions.TryGetValue(unchecked((int)wParam.ToInt64()), out action))
            {
                return false;
            }
        }

        RaiseShortcutInvoked(action);
        return true;
    }

    public void Unregister()
    {
        IntPtr windowHandle;
        int[] ids;
        lock (_gate)
        {
            windowHandle = _windowHandle;
            ids = _actions.Keys.ToArray();
            _actions.Clear();
            _windowHandle = IntPtr.Zero;
        }

        foreach (var id in ids)
            _ = _native.UnregisterHotKey(windowHandle, id);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
        }

        Unregister();
    }

    private static OperationResult<IReadOnlyList<PreparedShortcut>> PrepareRegistrations(
        IReadOnlyCollection<GlobalShortcutDefinition> definitions)
    {
        var registrations = new List<PreparedShortcut>(definitions.Count);
        var id = FirstShortcutId;
        foreach (var definition in definitions)
        {
            if (definition.VirtualKey is <= 0 or > 0xFF)
            {
                return OperationResult<IReadOnlyList<PreparedShortcut>>.Failure(
                    "Shortcut virtual key is invalid.");
            }

            if (!TryConvertModifiers(definition.Modifiers, out var modifiers))
            {
                return OperationResult<IReadOnlyList<PreparedShortcut>>.Failure(
                    "Shortcut modifiers are invalid.");
            }

            registrations.Add(new PreparedShortcut(id++, definition, modifiers));
        }

        return OperationResult<IReadOnlyList<PreparedShortcut>>.Success(registrations);
    }

    private static bool TryConvertModifiers(
        ShortcutModifiers modifiers,
        out uint nativeModifiers)
    {
        const ShortcutModifiers known = ShortcutModifiers.Alt |
            ShortcutModifiers.Control |
            ShortcutModifiers.Shift |
            ShortcutModifiers.Windows |
            ShortcutModifiers.NoRepeat;
        if ((modifiers & ~known) != 0)
        {
            nativeModifiers = 0;
            return false;
        }

        nativeModifiers = 0;
        if (modifiers.HasFlag(ShortcutModifiers.Alt))
            nativeModifiers |= ModAlt;
        if (modifiers.HasFlag(ShortcutModifiers.Control))
            nativeModifiers |= ModControl;
        if (modifiers.HasFlag(ShortcutModifiers.Shift))
            nativeModifiers |= ModShift;
        if (modifiers.HasFlag(ShortcutModifiers.Windows))
            nativeModifiers |= ModWindows;
        if (modifiers.HasFlag(ShortcutModifiers.NoRepeat))
            nativeModifiers |= ModNoRepeat;
        return true;
    }

    private void RollBack(IntPtr windowHandle, IReadOnlyList<int> registeredIds)
    {
        // 失败时逆序释放本轮已取得的系统热键所有权。
        for (var index = registeredIds.Count - 1; index >= 0; index--)
            _ = _native.UnregisterHotKey(windowHandle, registeredIds[index]);
    }

    private void RaiseShortcutInvoked(GlobalShortcutAction action)
    {
        var handlers = ShortcutInvoked;
        if (handlers is null)
            return;

        foreach (EventHandler<GlobalShortcutAction> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, action);
            }
            catch (Exception)
            {
                // 单个订阅者失败不能破坏全局快捷键消息链。
            }
        }
    }

    private sealed record PreparedShortcut(
        int Id,
        GlobalShortcutDefinition Definition,
        uint NativeModifiers);
}
