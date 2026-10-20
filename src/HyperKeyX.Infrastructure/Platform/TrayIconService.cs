using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class TrayIconService : ITrayIconService, ITrayNotificationService
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private const uint CallbackMessageId = 0x8051;
    private const uint IconId = 1;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int RightButtonUp = 0x0205;
    private const uint OpenCommandId = 1001;
    private const uint ToggleRuntimeCommandId = 1002;
    private const uint ExitCommandId = 1003;
    private const int MaximumTooltipLength = 127;

    private readonly object _gate = new();
    private readonly ITrayNativeApi _native;
    private readonly string _executablePath;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private TrayPresentation? _presentation;
    private bool _isDisposed;

    public TrayIconService()
        : this(
            new TrayNativeApi(),
            Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前程序路径。"))
    {
    }

    internal TrayIconService(ITrayNativeApi native, string executablePath)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
        _taskbarCreatedMessage = _native.RegisterWindowMessage(TaskbarCreatedMessageName);
    }

    public event EventHandler<TrayCommand>? CommandInvoked;

    public uint CallbackMessage => CallbackMessageId;

    public bool IsVisible { get; private set; }

    public OperationResult Show(IntPtr windowHandle, TrayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("Tray icon service is disposed.");
            if (windowHandle == IntPtr.Zero)
                return OperationResult.Failure("Window handle is invalid.");
            if (IsVisible)
                return UpdateCore(presentation);

            var iconHandle = _native.LoadSmallIcon(_executablePath);
            if (iconHandle == IntPtr.Zero)
                return NativeFailure("Could not load the application icon");

            var registration = CreateRegistration(windowHandle, iconHandle, presentation);
            if (!_native.AddIcon(registration))
            {
                _native.DestroyIcon(iconHandle);
                return NativeFailure("Could not add the tray icon");
            }

            _windowHandle = windowHandle;
            _iconHandle = iconHandle;
            _presentation = presentation;
            IsVisible = true;
            return OperationResult.Success();
        }
    }

    public OperationResult Update(TrayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("Tray icon service is disposed.");
            return UpdateCore(presentation);
        }
    }

    public bool TryHandleWindowMessage(
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            lock (_gate)
            {
                if (_isDisposed || !IsVisible || _presentation is null)
                    return true;

                // Explorer 重启会清空通知区域；复用当前所有权重新登记即可。
                var registration = CreateRegistration(
                    _windowHandle,
                    _iconHandle,
                    _presentation);
                _ = _native.AddIcon(registration);
                return true;
            }
        }

        if (message != CallbackMessage)
            return false;

        TrayCommand? command = null;
        lock (_gate)
        {
            if (_isDisposed || !IsVisible || _presentation is null)
                return true;

            var notification = unchecked((int)lParam.ToInt64()) & 0xFFFF;
            if (notification == LeftButtonDoubleClick)
            {
                command = TrayCommand.Open;
            }
            else if (notification == RightButtonUp)
            {
                command = _native.ShowContextMenu(_windowHandle, _presentation) switch
                {
                    OpenCommandId => TrayCommand.Open,
                    ToggleRuntimeCommandId => TrayCommand.ToggleRuntime,
                    ExitCommandId => TrayCommand.Exit,
                    _ => null
                };
            }
        }

        if (command is { } value)
            RaiseCommand(value);
        return true;
    }

    /// <summary>在已有图标上发送通知，锁内检查生命周期，隐藏后不重新创建图标。</summary>
    public OperationResult ShowNotification(LocalNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (_isDisposed || !IsVisible)
                return OperationResult.Failure("Tray icon is not available.");
            return _native.ShowNotification(_windowHandle, IconId,
                TruncateNotification(notification.Title, 63), TruncateNotification(notification.Message, 255))
                ? OperationResult.Success() : NativeFailure("Could not show tray notification");
        }
    }

    /// <summary>为原生定长 UTF-16 字段留出终止符，截断时不拆开代理项。</summary>
    private static string TruncateNotification(string value, int limit)
    {
        if (value.Length <= limit) return value;
        if (char.IsHighSurrogate(value[limit - 1])) limit--;
        return value[..limit];
    }

    public void Hide()
    {
        lock (_gate)
            HideCore();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return;

            HideCore();
            _isDisposed = true;
        }
    }

    private OperationResult UpdateCore(TrayPresentation presentation)
    {
        if (!IsVisible || _windowHandle == IntPtr.Zero || _iconHandle == IntPtr.Zero)
            return OperationResult.Failure("Tray icon is not visible.");

        var registration = CreateRegistration(_windowHandle, _iconHandle, presentation);
        if (!_native.UpdateIcon(registration))
            return NativeFailure("Could not update the tray icon");

        _presentation = presentation;
        return OperationResult.Success();
    }

    private void HideCore()
    {
        if (!IsVisible)
            return;

        _native.DeleteIcon(_windowHandle, IconId);
        if (_iconHandle != IntPtr.Zero)
            _native.DestroyIcon(_iconHandle);
        _windowHandle = IntPtr.Zero;
        _iconHandle = IntPtr.Zero;
        _presentation = null;
        IsVisible = false;
    }

    private TrayIconRegistration CreateRegistration(
        IntPtr windowHandle,
        IntPtr iconHandle,
        TrayPresentation presentation) =>
        new(
            windowHandle,
            IconId,
            CallbackMessage,
            iconHandle,
            NormalizeTooltip(presentation.Tooltip));

    private OperationResult NativeFailure(string operation)
    {
        var error = _native.GetLastError();
        return OperationResult.Failure($"{operation} (Win32 error {error}).");
    }

    private void RaiseCommand(TrayCommand command)
    {
        var handlers = CommandInvoked;
        if (handlers is null)
            return;

        foreach (EventHandler<TrayCommand> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, command);
            }
            catch (Exception)
            {
                // 单个命令订阅者失败不能阻断窗口恢复或显式退出。
            }
        }
    }

    private static string NormalizeTooltip(string value)
    {
        value ??= string.Empty;
        return value.Length <= MaximumTooltipLength
            ? value
            : value[..MaximumTooltipLength];
    }
}
