using System.ComponentModel;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Updates;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;
using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Shell.Appearance;
using HyperKeyX.Shell.Dialogs;
using HyperKeyX.Shell.Notifications;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace HyperKeyX.Shell;

/// <summary>
/// 主窗口负责窗口尺寸约束以及页面和服务的确定性释放。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly GlobalShortcutDefinition ToggleRuntimeShortcut;

    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 960;
    private const int MinimumWindowWidth = 960;
    private const int MinimumWindowHeight = 720;
    private const uint DefaultDpi = 96;
    private const int GWLP_WNDPROC = -4;
    private const int WM_GETMINMAXINFO = 0x0024;

    private readonly MainViewModel _viewModel;
    private readonly ITrayIconService _trayIcon;
    private readonly IGlobalShortcutService _globalShortcut;
    private readonly ILocalNotificationService _localNotifications;
    private readonly IWindowAppearanceService _appearanceService;
    private readonly IUnsavedChangesPrompt _unsavedChangesPrompt;
    private readonly IMsiUpdateInstaller _updateInstaller;
    private readonly MainPage _mainPage;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly AppWindow _appWindow;
    private readonly WindowProc _windowProc;
    private IntPtr _oldWindowProc;
    private IntPtr _windowHandle;
    private bool _exitRequested;
    private bool _exitDecisionPending;
    private bool _closed;
    private bool _isApplyingAppearance;
    private bool _shellServicesInitialized;
    private bool _notificationWarningShown;

    public MainWindow(
        MainViewModel viewModel,
        ITrayIconService trayIcon,
        IGlobalShortcutService globalShortcut,
        ILocalNotificationService localNotifications,
        IWindowAppearanceService appearanceService,
        IUnsavedChangesPrompt unsavedChangesPrompt,
        IMsiUpdateInstaller updateInstaller)
    {
        InitializeComponent();
        _viewModel = viewModel;
        var shortcut = viewModel.Settings.ActiveShortcuts.ToggleRuntime;
        ToggleRuntimeShortcut = new(GlobalShortcutAction.ToggleRuntime,
            (ShortcutModifiers)shortcut.Modifiers | ShortcutModifiers.NoRepeat, shortcut.Key);
        _trayIcon = trayIcon;
        _globalShortcut = globalShortcut;
        _localNotifications = localNotifications;
        if (localNotifications is WindowsLocalNotificationService windowsNotifications)
            windowsNotifications.SetFallback(ShowFallbackNotification);
        _appearanceService = appearanceService;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _updateInstaller = updateInstaller;
        _viewModel.Settings.InstallUpdateRequested = ConfirmAndInstallUpdateAsync;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ??
            throw new InvalidOperationException("无法取得主窗口调度队列。");
        Title = _viewModel.WindowTitleText;
        AppTitleText.Text = _viewModel.WindowTitleText;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _appearanceService.SystemAppearanceChanged += OnSystemAppearanceChanged;

        ExtendsContentIntoTitleBar = true;
        _windowProc = WindowProcHandler;
        _appWindow = ConfigureWindowBounds();
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "HyperKeyX.ico"));
        _mainPage = new MainPage(viewModel);
        PageHost.Content = _mainPage;
        _appWindow.Closing += OnAppWindowClosing;
        _trayIcon.CommandInvoked += OnTrayCommandInvoked;
        _globalShortcut.ShortcutInvoked += OnGlobalShortcutInvoked;
        _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        ConfigureTitleBar();
        ApplyWindowTheme();

        ApplyAppearance();
        Closed += OnClosed;
        Activated += OnWindowActivated;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WindowTitleText))
        {
            Title = _viewModel.WindowTitleText;
            AppTitleText.Text = _viewModel.WindowTitleText;
        }
        else if (e.PropertyName == nameof(MainViewModel.AppTheme))
        {
            ApplyWindowTheme();
        }
        else if (e.PropertyName is nameof(MainViewModel.IsEnglish) or
                 nameof(MainViewModel.IsOrchestratorRunning))
        {
            UpdateTrayPresentation();
        }
        else if (e.PropertyName == nameof(MainViewModel.WasEmergencyStopped) &&
                 _viewModel.WasEmergencyStopped)
        {
            var notification = RuntimeNotificationFactory.CreateEmergencyStop(
                _viewModel.IsEnglish);
            _dispatcherQueue.TryEnqueue(() => ShowRuntimeNotification(notification));
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsShortcutCaptureActive))
            SynchronizeShortcutCapture();
        if (e.PropertyName == nameof(SettingsViewModel.TrayIconEnabled))
            SynchronizeTrayPreference();

        if (e.PropertyName == nameof(SettingsViewModel.RuntimeStatusNotificationsEnabled) &&
            _viewModel.Settings.RuntimeStatusNotificationsEnabled)
        {
            EnsureLocalNotificationsInitialized();
        }

        if (IsAppearanceProperty(e.PropertyName))
            ApplyAppearance();
    }

    private void OnGlobalShortcutInvoked(object? sender, GlobalShortcutAction action)
    {
        if (!_closed && action == GlobalShortcutAction.ToggleRuntime)
            _dispatcherQueue.TryEnqueue(ToggleRuntimeFromShortcut);
    }

    private void ToggleRuntimeFromShortcut()
    {
        if (_closed || _viewModel.Settings.IsShortcutCaptureActive)
            return;

        var wasRunning = _viewModel.IsOrchestratorRunning;
        _viewModel.ToggleOrchestratorCommand.Execute(null);
        var isRunning = _viewModel.IsOrchestratorRunning;
        var notification = RuntimeNotificationFactory.CreateToggleTransition(
            wasRunning,
            isRunning,
            _viewModel.IsEnglish,
            _viewModel.ActiveRuntimeProfileName);
        if (notification is not null)
            ShowRuntimeNotification(notification);
    }

    /// <summary>窗口失活时恢复全局键，返回捕获框时再次暂停，避免后台遗留暂停状态。</summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) =>
        _viewModel.Settings.IsShortcutCaptureActive = args.WindowActivationState != WindowActivationState.Deactivated &&
            WindowRoot.IsLoaded && WindowRoot.XamlRoot is { } xamlRoot &&
            Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) is TextBox { Tag: "ShortcutCapture" };

    /// <summary>捕获期间释放系统注册，使原快捷键也能作为普通按键送达捕获框。</summary>
    private void SynchronizeShortcutCapture()
    {
        if (_closed || !_shellServicesInitialized) return;
        if (_viewModel.Settings.IsShortcutCaptureActive)
        {
            _globalShortcut.Unregister();
            return;
        }
        var result = _globalShortcut.Register(_windowHandle, [ToggleRuntimeShortcut]);
        if (!result.Succeeded) ShowShortcutError(result);
    }

    private void EnsureLocalNotificationsInitialized()
    {
        var result = _localNotifications.Initialize();
        if (!result.Succeeded)
            ShowNotificationWarningOnce(result);
    }

    private void ShowRuntimeNotification(LocalNotification notification)
    {
        if (_closed || !_viewModel.Settings.RuntimeStatusNotificationsEnabled)
            return;

        var result = _localNotifications.Show(notification);
        if (!result.Succeeded)
            ShowNotificationWarningOnce(result);
    }

    /// <summary>管理员模式复用现有托盘；用户关闭托盘时在应用内显示同一状态消息。</summary>
    private OperationResult ShowFallbackNotification(LocalNotification notification)
    {
        if (_closed) return OperationResult.Failure("Window is closed.");
        if (_trayIcon.IsVisible && _trayIcon is ITrayNotificationService trayNotifications)
            return trayNotifications.ShowNotification(notification);
        RuntimeNotificationBar.Title = notification.Title;
        RuntimeNotificationBar.Message = notification.Message;
        RuntimeNotificationBar.IsOpen = true;
        return OperationResult.Success();
    }

    private void ShowNotificationWarningOnce(OperationResult result)
    {
        if (_notificationWarningShown || _closed)
            return;

        _notificationWarningShown = true;
        var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? (_viewModel.IsEnglish ? "Unknown error" : "未知错误")
            : result.ErrorMessage;
        _viewModel.Settings.ShowWarning(_viewModel.IsEnglish
            ? $"Could not show Windows notifications: {detail}"
            : $"无法显示 Windows 通知：{detail}");
    }

    private void ShowShortcutError(OperationResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? (_viewModel.IsEnglish ? "Unknown error" : "未知错误")
            : result.ErrorMessage;
        _viewModel.Settings.ShowWarning(_viewModel.IsEnglish
            ? $"Could not register {_viewModel.Settings.ActiveShortcuts.ToggleRuntime}: {detail}"
            : $"无法注册 {_viewModel.Settings.ActiveShortcuts.ToggleRuntime}：{detail}");
    }

    private void OnSystemAppearanceChanged(object? sender, EventArgs e) =>
        _dispatcherQueue.TryEnqueue(ApplyAppearance);

    public async Task InitializeSystemSettingsAsync()
    {
        await _viewModel.Settings.InitializeSystemSettingsAsync();
        SynchronizeTrayPreference();

        if (_shellServicesInitialized)
            return;

        _shellServicesInitialized = true;
        SynchronizeShortcutCapture();

        if (_viewModel.Settings.RuntimeStatusNotificationsEnabled)
            EnsureLocalNotificationsInitialized();
    }

    public void ShowFromTray()
    {
        _appWindow.Show();
        Activate();
    }

    public void RequestShowFromSecondary() =>
        _dispatcherQueue.TryEnqueue(ShowFromTray);

    public void RequestExit() => _ = RequestExitAsync();

    private async Task RequestExitAsync()
    {
        if (_exitRequested || _exitDecisionPending)
            return;

        _exitDecisionPending = true;
        try
        {
            var canExit = await UnsavedChangesExitCoordinator.CanExitAsync(
                _viewModel.HasUnsavedChanges,
                () => _unsavedChangesPrompt.ShowAsync(
                    WindowRoot.XamlRoot,
                    _viewModel.IsEnglish),
                _viewModel.SaveAllChanges);
            if (!canExit)
                return;

            CompleteExit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainWindow] 无法处理退出确认：{ex}");
            _viewModel.Settings.ShowWarning(_viewModel.IsEnglish
                ? "Could not confirm application exit."
                : "无法确认是否退出应用程序。");
        }
        finally
        {
            _exitDecisionPending = false;
        }
    }

    /// <summary>明确确认并处理未保存编辑后，等待安装助手就绪才真正退出。</summary>
    private async Task<OperationResult> ConfirmAndInstallUpdateAsync(PreparedUpdate package)
    {
        if (_closed || _exitRequested || _exitDecisionPending)
            return OperationResult.Failure("当前无法退出并安装。");
        _exitDecisionPending = true;
        try
        {
            var english = _viewModel.IsEnglish;
            var dialog = new ContentDialog
            {
                XamlRoot = WindowRoot.XamlRoot,
                RequestedTheme = _viewModel.AppTheme,
                Title = english ? "Install update" : "安装更新",
                Content = english ? $"Exit HyperKeyX and install {package.Version}?" : $"退出 HyperKeyX 并安装 {package.Version}？",
                PrimaryButtonText = english ? "Exit and install" : "退出并安装",
                CloseButtonText = english ? "Cancel" : "取消",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return OperationResult.Success();
            if (!await UnsavedChangesExitCoordinator.CanExitAsync(_viewModel.HasUnsavedChanges,
                () => _unsavedChangesPrompt.ShowAsync(WindowRoot.XamlRoot, _viewModel.IsEnglish), _viewModel.SaveAllChanges))
                return OperationResult.Success();
            var result = await _updateInstaller.StartAfterExitAsync(package);
            if (result.Succeeded) CompleteExit();
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 无法交接 MSI 更新：{ex}");
            return OperationResult.Failure("无法启动更新安装。");
        }
        finally { _exitDecisionPending = false; }
    }

    private void CompleteExit()
    {
        _exitRequested = true;
        _trayIcon.Hide();
        Close();
    }

    public void ApplyLaunchMode(bool backgroundLaunch)
    {
        if (backgroundLaunch &&
            _viewModel.Settings.TrayIconEnabled &&
            _viewModel.Settings.StartMinimizedToTray)
        {
            HideToTray();
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
            return;

        args.Cancel = true;
        if (!_viewModel.Settings.TrayIconEnabled)
        {
            RequestExit();
            return;
        }

        var result = EnsureTrayVisible();
        if (!result.Succeeded)
        {
            ShowTrayError(result);
            return;
        }

        // 收起到托盘只是隐藏窗口，编辑草稿和撤销历史继续保留。
        _appWindow.Hide();
    }

    private void OnTrayCommandInvoked(object? sender, TrayCommand command)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            switch (command)
            {
                case TrayCommand.Open:
                    ShowFromTray();
                    break;
                case TrayCommand.ToggleRuntime:
                    _viewModel.ToggleOrchestratorCommand.Execute(null);
                    UpdateTrayPresentation();
                    break;
                case TrayCommand.Exit:
                    RequestExit();
                    break;
            }
        });
    }

    private void HideToTray()
    {
        var result = EnsureTrayVisible();
        if (!result.Succeeded)
        {
            ShowTrayError(result);
            return;
        }

        _appWindow.Hide();
    }

    private void SynchronizeTrayPreference()
    {
        if (!_viewModel.Settings.TrayIconEnabled)
        {
            _trayIcon.Hide();
            return;
        }

        var result = EnsureTrayVisible();
        if (result.Succeeded)
            return;

        // 创建托盘图标失败时回滚偏好，避免界面显示已启用但实际不可用。
        _viewModel.Settings.SetTrayIconEnabled(false);
        ShowTrayError(result);
    }

    private OperationResult EnsureTrayVisible()
    {
        var presentation = TrayPresentationFactory.Create(
            _viewModel.IsEnglish,
            _viewModel.IsOrchestratorRunning);
        return _trayIcon.IsVisible
            ? _trayIcon.Update(presentation)
            : _trayIcon.Show(_windowHandle, presentation);
    }

    private void UpdateTrayPresentation()
    {
        if (!_trayIcon.IsVisible)
            return;

        var result = _trayIcon.Update(TrayPresentationFactory.Create(
            _viewModel.IsEnglish,
            _viewModel.IsOrchestratorRunning));
        if (!result.Succeeded)
            ShowTrayError(result);
    }

    private void ShowTrayError(OperationResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? (_viewModel.IsEnglish ? "Unknown error" : "未知错误")
            : result.ErrorMessage;
        _viewModel.Settings.ShowWarning(_viewModel.IsEnglish
            ? $"Could not use the tray icon: {detail}"
            : $"无法使用托盘图标：{detail}");
    }

    private AppWindow ConfigureWindowBounds()
    {
        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var workArea = GetCurrentWorkArea(_windowHandle);

        var dpi = Math.Max(GetDpiForWindow(_windowHandle), DefaultDpi);
        var fittedSize = FitWithinWorkArea(
            ScaleForDpi(DefaultWindowWidth, dpi),
            ScaleForDpi(DefaultWindowHeight, dpi),
            workArea);
        var x = workArea.X + Math.Max((workArea.Width - fittedSize.Width) / 2, 0);
        var y = workArea.Y + Math.Max((workArea.Height - fittedSize.Height) / 2, 0);
        appWindow.MoveAndResize(new RectInt32(x, y, fittedSize.Width, fittedSize.Height));

        _oldWindowProc = SetWindowLongPtr(
            _windowHandle,
            GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_windowProc));
        if (_oldWindowProc == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装窗口尺寸处理器。");

        return appWindow;
    }

    private void ConfigureTitleBar()
    {
        SetTitleBar(AppTitleBarDragRegion);
        AppTitleBar.SizeChanged += OnAppTitleBarSizeChanged;
        WindowRoot.ActualThemeChanged += OnWindowRootActualThemeChanged;
        UpdateTitleBarInsets(_appWindow.TitleBar);
        UpdateTitleBarButtonColors();
    }

    private void ApplyWindowTheme()
    {
        WindowRoot.RequestedTheme = _viewModel.AppTheme;
        UpdateTitleBarButtonColors();
    }

    private void OnAppTitleBarSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateTitleBarInsets(_appWindow.TitleBar);

    private void OnWindowRootActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTitleBarButtonColors();
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        if (_isApplyingAppearance || _closed)
            return;

        _isApplyingAppearance = true;
        try
        {
            // 滑块拖动时只预览遮罩强度，交互结束后才写入偏好文件。
            var preferences = _viewModel.Settings.CurrentPreferences with
            {
                BackgroundOverlayOpacity = _viewModel.Settings.BackgroundOverlayOpacity
            };
            var result = _appearanceService.Apply(
                this,
                _mainPage.CreateAppearanceHost(WindowRoot),
                preferences);
            // 显式更新继承字体，使已创建的页面立即响应，而非等待主题资源重建。
            var font = new Microsoft.UI.Xaml.Media.FontFamily(
                HyperKeyX.Domain.Appearance.AppearanceOptionResolver.FontFamilyName(preferences));
            PageHost.FontFamily = font;
            AppTitleText.FontFamily = font;
            if (!result.Succeeded && result.WarningMessage is not null)
                _viewModel.Settings.ShowWarning(result.WarningMessage);
        }
        finally
        {
            _isApplyingAppearance = false;
        }
    }

    private static bool IsAppearanceProperty(string? propertyName) => propertyName is
        nameof(SettingsViewModel.SelectedThemeIndex) or
        nameof(SettingsViewModel.SelectedFontIndex) or
        nameof(SettingsViewModel.SelectedFontFamilyName) or
        nameof(SettingsViewModel.SelectedAccentModeIndex) or
        nameof(SettingsViewModel.CustomAccentColor) or
        nameof(SettingsViewModel.SelectedBackdropIndex) or
        nameof(SettingsViewModel.CustomBackgroundEnabled) or
        nameof(SettingsViewModel.CustomBackgroundAssetName) or
        nameof(SettingsViewModel.BackgroundOverlayOpacity);

    private void UpdateTitleBarInsets(AppWindowTitleBar titleBar)
    {
        // 系统保留区会随 DPI、窗口状态和标题按钮宽度改变。
        TitleBarLeftInsetColumn.Width = new GridLength(titleBar.LeftInset);
        TitleBarRightInsetColumn.Width = new GridLength(titleBar.RightInset);
    }

    private void UpdateTitleBarButtonColors()
    {
        var isDark = WindowRoot.ActualTheme == ElementTheme.Dark;
        var foreground = isDark ? Colors.White : Colors.Black;
        var inactiveForeground = isDark ? Colors.DarkGray : Colors.Gray;
        var hoverBackground = isDark
            ? ColorHelper.FromArgb(32, 255, 255, 255)
            : ColorHelper.FromArgb(24, 0, 0, 0);
        var pressedBackground = isDark
            ? ColorHelper.FromArgb(48, 255, 255, 255)
            : ColorHelper.FromArgb(40, 0, 0, 0);
        var titleBar = _appWindow.TitleBar;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private IntPtr WindowProcHandler(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_globalShortcut.TryHandleWindowMessage(message, wParam, lParam))
            return IntPtr.Zero;

        if (_trayIcon.TryHandleWindowMessage(message, wParam, lParam))
            return IntPtr.Zero;

        if (message == WM_GETMINMAXINFO)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var dpi = Math.Max(GetDpiForWindow(hwnd), DefaultDpi);
            var workArea = GetCurrentWorkArea(hwnd);

            // 最小尺寸按 DPI 缩放，同时不超过当前显示器工作区。
            minMaxInfo.ptMinTrackSize.x = Math.Min(
                ScaleForDpi(MinimumWindowWidth, dpi),
                workArea.Width);
            minMaxInfo.ptMinTrackSize.y = Math.Min(
                ScaleForDpi(MinimumWindowHeight, dpi),
                workArea.Height);
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return IntPtr.Zero;
        }

        return _oldWindowProc != IntPtr.Zero
            ? CallWindowProc(_oldWindowProc, hwnd, message, wParam, lParam)
            : DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed)
            return;

        _closed = true;
        Closed -= OnClosed;
        Activated -= OnWindowActivated;
        _appWindow.Closing -= OnAppWindowClosing;
        _trayIcon.CommandInvoked -= OnTrayCommandInvoked;
        _globalShortcut.ShortcutInvoked -= OnGlobalShortcutInvoked;
        _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _viewModel.Settings.InstallUpdateRequested = null;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _appearanceService.SystemAppearanceChanged -= OnSystemAppearanceChanged;
        AppTitleBar.SizeChanged -= OnAppTitleBarSizeChanged;
        WindowRoot.ActualThemeChanged -= OnWindowRootActualThemeChanged;
        _globalShortcut.Unregister();
        _localNotifications.Dispose();
        _trayIcon.Hide();
        _viewModel.Dispose();

        if (_windowHandle != IntPtr.Zero && _oldWindowProc != IntPtr.Zero)
        {
            _ = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _oldWindowProc);
            _oldWindowProc = IntPtr.Zero;
        }

        App.ShutdownServices();
    }

    private static int ScaleForDpi(int logicalPixels, uint dpi)
    {
        return (int)Math.Round(logicalPixels * dpi / (double)DefaultDpi);
    }

    private static RectInt32 GetCurrentWorkArea(IntPtr windowHandle)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        return DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest).WorkArea;
    }

    private static SizeInt32 FitWithinWorkArea(int width, int height, RectInt32 workArea)
    {
        var factor = Math.Min(
            1.0,
            Math.Min(workArea.Width / (double)width, workArea.Height / (double)height));
        return new SizeInt32(
            Math.Max((int)Math.Round(width * factor), 1),
            Math.Max((int)Math.Round(height * factor), 1));
    }

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProc,
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
