using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    private IStartupRegistrationService _startupRegistration =
        UnavailableStartupRegistrationService.Instance;
    private readonly CancellationTokenSource _lifecycleStopping = new();
    private bool _startWithWindows;
    private bool _trayIconEnabled;
    private bool _startMinimizedToTray;
    private bool _isLifecycleOperationRunning;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        private set => SetProperty(ref _startWithWindows, value);
    }

    public bool TrayIconEnabled
    {
        get => _trayIconEnabled;
        private set
        {
            if (SetProperty(ref _trayIconEnabled, value))
                OnPropertyChanged(nameof(CanStartMinimizedToTray));
        }
    }

    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        private set => SetProperty(ref _startMinimizedToTray, value);
    }

    public bool IsLifecycleOperationRunning
    {
        get => _isLifecycleOperationRunning;
        private set
        {
            if (SetProperty(ref _isLifecycleOperationRunning, value))
            {
                OnPropertyChanged(nameof(IsLifecycleOperationIdle));
                OnPropertyChanged(nameof(CanStartMinimizedToTray));
            }
        }
    }

    public bool IsLifecycleOperationIdle => !IsLifecycleOperationRunning;
    public bool CanStartMinimizedToTray =>
        TrayIconEnabled && IsLifecycleOperationIdle;

    public string StartWithWindowsText => Localize("开机启动", "Start with Windows");
    public string StartWithWindowsDescriptionText => Localize(
        "用户登录后以最高权限启动应用",
        "Launch the app with highest privileges after sign-in");
    public string TrayIconText => Localize("启用托盘图标", "Enable tray icon");
    public string TrayIconDescriptionText => Localize(
        "关闭主窗口时保留后台运行",
        "Keep the app running when the main window is closed");
    public string StartMinimizedToTrayText => Localize(
        "启动时最小化到托盘",
        "Start minimized to tray");
    public string StartMinimizedToTrayDescriptionText => Localize(
        "通过开机任务启动时不显示主窗口",
        "Hide the main window when launched by the startup task");
    public string ToggleOnText => Localize("开", "On");
    public string ToggleOffText => Localize("关", "Off");

    public async Task InitializeSystemSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed || IsLifecycleOperationRunning)
            return;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleStopping.Token);
        var operationToken = linkedCancellation.Token;
        IsLifecycleOperationRunning = true;
        try
        {
            var result = await _startupRegistration.IsEnabledAsync(operationToken);
            operationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded)
            {
                ShowLifecycleError(
                    "无法读取开机启动状态",
                    "Could not read startup registration",
                    result.ErrorMessage);
                return;
            }

            if (result.Value == StartWithWindows)
                return;

            if (TrySave(_preferences with { StartWithWindows = result.Value }))
                StartWithWindows = result.Value;
        }
        catch (OperationCanceledException) when (_lifecycleStopping.IsCancellationRequested)
        {
            // 应用释放时安静终止平台查询，不再更新已关闭窗口的状态。
        }
        finally
        {
            IsLifecycleOperationRunning = false;
        }
    }

    public async Task SetStartWithWindowsAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed || IsLifecycleOperationRunning || StartWithWindows == enabled)
            return;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleStopping.Token);
        var operationToken = linkedCancellation.Token;
        IsLifecycleOperationRunning = true;
        try
        {
            var result = await _startupRegistration.SetEnabledAsync(enabled, operationToken);
            operationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded)
            {
                ShowLifecycleError(
                    "无法修改开机启动",
                    "Could not change startup registration",
                    result.ErrorMessage);
                return;
            }

            if (!TrySave(_preferences with { StartWithWindows = enabled }))
            {
                // 偏好保存失败时恢复系统任务，避免两个状态长期不一致。
                var rollback = await _startupRegistration.SetEnabledAsync(
                    !enabled,
                    operationToken);
                if (!rollback.Succeeded)
                {
                    ShowLifecycleError(
                        "设置保存失败，且无法恢复开机启动",
                        "Settings could not be saved and startup registration could not be restored",
                        rollback.ErrorMessage);
                }

                return;
            }

            StartWithWindows = enabled;
        }
        catch (OperationCanceledException) when (_lifecycleStopping.IsCancellationRequested)
        {
            // 释放期间不再把取消传播到 WinUI 启动或关闭流程。
        }
        finally
        {
            IsLifecycleOperationRunning = false;
        }
    }

    public void SetTrayIconEnabled(bool enabled)
    {
        if (_isDisposed || IsLifecycleOperationRunning || TrayIconEnabled == enabled)
            return;

        var candidate = _preferences with
        {
            TrayIconEnabled = enabled,
            StartMinimizedToTray = enabled && StartMinimizedToTray
        };
        if (!TrySave(candidate))
            return;

        TrayIconEnabled = enabled;
        if (!enabled)
            StartMinimizedToTray = false;
    }

    public void SetStartMinimizedToTray(bool enabled)
    {
        if (_isDisposed || IsLifecycleOperationRunning || StartMinimizedToTray == enabled)
            return;

        if (enabled && !TrayIconEnabled)
        {
            ShowError(Localize(
                "请先启用托盘图标。",
                "Enable the tray icon before starting minimized."));
            return;
        }

        if (TrySave(_preferences with { StartMinimizedToTray = enabled }))
            StartMinimizedToTray = enabled;
    }

    private void InitializeLifecycle(IStartupRegistrationService startupRegistration)
    {
        _startupRegistration = startupRegistration ??
            throw new ArgumentNullException(nameof(startupRegistration));
        _startWithWindows = _preferences.StartWithWindows;
        _trayIconEnabled = _preferences.TrayIconEnabled;
        _startMinimizedToTray = _preferences.StartMinimizedToTray;
    }

    private void StopLifecycleOperations() => _lifecycleStopping.Cancel();

    private void ShowLifecycleError(
        string chinesePrefix,
        string englishPrefix,
        string? detail)
    {
        detail = string.IsNullOrWhiteSpace(detail)
            ? Localize("未知错误", "Unknown error")
            : detail;
        var separator = IsEnglish ? ": " : "：";
        ShowError($"{Localize(chinesePrefix, englishPrefix)}{separator}{detail}");
    }

    private sealed class UnavailableStartupRegistrationService : IStartupRegistrationService
    {
        public static UnavailableStartupRegistrationService Instance { get; } = new();

        public Task<OperationResult<bool>> IsEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult<bool>.Failure("开机启动服务不可用。"));
        }

        public Task<OperationResult> SetEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult.Failure("开机启动服务不可用。"));
        }
    }
}
