using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Shell.Notifications;

/// <summary>
/// 管理非打包 WinUI 应用的本地通知注册，平台失败不会影响自动化运行状态。
/// </summary>
public sealed class WindowsLocalNotificationService : ILocalNotificationService
{
    private const string DisplayName = "HyperKeyX";
    private const string NotificationLogoName = "Square44x44Logo.scale-200.png";

    private readonly object _gate = new();
    private readonly IAppNotificationPlatform _platform;
    private readonly Uri _iconUri;
    private OperationResult? _initializationResult;
    private bool _initializationAttempted;
    private bool _isRegistered;
    private bool _isDisposed;
    private bool _useFallback;
    private Func<LocalNotification, OperationResult>? _fallback;

    /// <summary>窗口在首次初始化前提供回退呈现，不取得托盘或窗口的所有权。</summary>
    internal void SetFallback(Func<LocalNotification, OperationResult> fallback)
    {
        lock (_gate)
        {
            if (_initializationAttempted || _isDisposed)
                throw new InvalidOperationException("Notification initialization has already started.");
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }
    }

    public WindowsLocalNotificationService()
        : this(new AppNotificationPlatform(), CreateDefaultIconUri())
    {
    }

    internal WindowsLocalNotificationService(
        IAppNotificationPlatform platform,
        Uri iconUri, Func<LocalNotification, OperationResult>? fallback = null)
    {
        _fallback = fallback;
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _iconUri = iconUri ?? throw new ArgumentNullException(nameof(iconUri));
        if (!_iconUri.IsAbsoluteUri || !_iconUri.IsFile)
            throw new ArgumentException("Notification icon URI must be a local file.", nameof(iconUri));
    }

    public bool IsInitialized
    {
        get
        {
            lock (_gate)
                return (_isRegistered || _useFallback) && !_isDisposed;
        }
    }

    public OperationResult Initialize()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("Local notification service is disposed.");
            if (_initializationResult is { } cached)
                return cached;
            if (_initializationAttempted)
            {
                return OperationResult.Failure(
                    "Local notification initialization is in progress.");
            }

            _initializationAttempted = true;
        }

        var result = TryInitializePlatform();
        var unregisterAfterDispose = false;
        lock (_gate)
        {
            if (_isDisposed && result.Succeeded)
            {
                unregisterAfterDispose = !_useFallback;
                result = OperationResult.Failure("Local notification service is disposed.");
            }
            else
            {
                _isRegistered = result.Succeeded && !_useFallback;
            }

            _initializationResult = result;
        }

        if (unregisterAfterDispose)
            TryUnregisterPlatform();
        return result;
    }

    public OperationResult Show(LocalNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var initialized = Initialize();
        if (!initialized.Succeeded)
            return initialized;

        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("Local notification service is disposed.");
        }

        try
        {
            lock (_gate)
            {
                if (_isDisposed) return OperationResult.Failure("Local notification service is disposed.");
                if (_useFallback && _fallback is { } fallback) return fallback(notification);
            }
            if (!_platform.IsEnabled())
                return OperationResult.Failure("Windows app notifications are disabled.");

            _platform.Show(notification);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(
                $"Could not show Windows app notification: {ex.Message}");
        }
    }

    public void Dispose()
    {
        bool unregister;
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            unregister = _isRegistered;
            _isRegistered = false;
            _fallback = null;
        }

        if (unregister)
            TryUnregisterPlatform();
    }

    private OperationResult TryInitializePlatform()
    {
        try
        {
            if (!_platform.IsSupported())
            {
                lock (_gate)
                {
                    if (!_isDisposed && _fallback is not null)
                    {
                        _useFallback = true;
                        return OperationResult.Success();
                    }
                }
                return OperationResult.Failure(
                    "Windows app notifications are not supported.");
            }

            _platform.Register(DisplayName, _iconUri);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(
                $"Could not register Windows app notifications: {ex.Message}");
        }
    }

    private void TryUnregisterPlatform()
    {
        try
        {
            _platform.Unregister();
        }
        catch (Exception)
        {
            // 进程退出时注销失败不能阻断其余服务释放。
        }
    }

    private static Uri CreateDefaultIconUri() => new(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        NotificationLogoName)));
}
