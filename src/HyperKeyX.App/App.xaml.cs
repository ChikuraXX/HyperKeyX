using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Abstractions.Processes;
using HyperKeyX.Application.Abstractions.Storage;
using HyperKeyX.Application.Automation;
using HyperKeyX.Application.Automation.Recording;
using HyperKeyX.Application.Automation.Paths;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Infrastructure.Updates;
using HyperKeyX.Application.Platform;
using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Infrastructure.Appearance;
using HyperKeyX.Infrastructure.Input;
using HyperKeyX.Infrastructure.Persistence;
using HyperKeyX.Infrastructure.Platform;
using HyperKeyX.Infrastructure.Processes;
using HyperKeyX.Infrastructure.Storage;
using HyperKeyX.Infrastructure.Windows;
using HyperKeyX.Shell;
using HyperKeyX.Shell.Appearance;
using HyperKeyX.Shell.Dialogs;
using HyperKeyX.Shell.Dispatching;
using HyperKeyX.Shell.Notifications;
using HyperKeyX.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace HyperKeyX;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static ServiceProvider? _serviceProvider;

    public static IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("应用服务尚未初始化或已释放。");
    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IBackgroundAssetService, BackgroundAssetService>();
        services.AddSingleton<IInstalledFontCatalog, HyperKeyX.Infrastructure.Appearance.WindowsFontCatalog>();
        services.AddSingleton<IWindowAppearanceService, WindowAppearanceService>();
        services.AddSingleton<IPathLauncher, PathLauncher>();
        services.AddSingleton<IExternalLinkLauncher, ExternalLinkLauncher>();
        services.AddSingleton(_ => new System.Net.Http.HttpClient());
        services.AddSingleton<IReleaseUpdateService, GitHubReleaseUpdateService>();
        services.AddSingleton<IUpdatePackageDownloader, MsiUpdateDownloader>();
        services.AddSingleton<IMsiUpdateInstaller, MsiUpdateInstaller>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IGlobalShortcutService, GlobalShortcutService>();
        services.AddSingleton<ILocalNotificationService, WindowsLocalNotificationService>();
        services.AddSingleton<IUnsavedChangesPrompt, UnsavedChangesPrompt>();
        services.AddSingleton<IConfigurationImportPrompt, ConfigurationImportPrompt>();
        services.AddSingleton<IUiDispatcher, DispatcherQueueUiDispatcher>();
        services.AddSingleton<IAppPreferencesStore, JsonAppPreferencesStore>();
        services.AddSingleton<IStorageInspector, StorageInspector>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton(provider => provider.GetRequiredService<SettingsViewModel>().ActiveShortcuts);
        services.AddSingleton<IConfigurationStore, JsonConfigurationStore>();
        services.AddSingleton<IConfigurationTransferService, JsonConfigurationTransferService>();
        services.AddSingleton<IMacroPackageTransferService, JsonMacroPackageTransferService>();
        services.AddSingleton<IWin32InputService, Win32InputService>();
        services.AddSingleton<IMouseInputService, Win32MouseInputService>();
        services.AddSingleton<IGlobalHookService, GlobalHookService>();
        services.AddSingleton<IGlobalMouseHookService, GlobalMouseHookService>();
        ConfigurePathServices(services);
        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        services.AddSingleton<IProcessNameResolver, ProcessNameResolver>();
        services.AddSingleton<IRuntimeProfileCatalog, RuntimeProfileCatalog>();
        services.AddSingleton<ITurboOrchestratorService, TurboOrchestratorService>();
        services.AddSingleton<IAutomationRuntimeCoordinator, AutomationRuntimeCoordinator>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    internal static void ConfigurePathServices(IServiceCollection services)
    {
        // 所有产生输入的会话共享同一执行权，避免录制、轨迹和旧自动化互相穿插。
        services.AddSingleton<AutomationInputLease>();
        services.AddSingleton<IWindowNativeApi, WindowNativeApi>();
        services.AddSingleton<IWindowTargetService, Win32WindowTargetService>();
        services.AddSingleton<IPointerInputService, Win32PointerInputService>();
        services.AddSingleton<IPhysicalInputStateService, Win32PhysicalInputStateService>();
        services.AddSingleton<IMousePathClock, MonotonicMousePathClock>();
        services.AddSingleton<RecordingControlRegions>();
        services.AddSingleton<IMousePathRecordingEnvironment, MousePathRecordingEnvironment>();
        services.AddSingleton<MousePathPlaybackService>();
        services.AddSingleton<MacroRecordingService>();
        services.AddSingleton<MousePathRecordingService>();
        services.AddSingleton<MacroRecordingCoordinator>(provider => new(
            provider.GetRequiredService<MacroRecordingService>(),
            provider.GetRequiredService<MousePathRecordingService>(),
            provider.GetRequiredService<AutomationInputLease>()));
        services.AddSingleton<IMacroRecordingService>(provider => provider.GetRequiredService<MacroRecordingCoordinator>());
        services.AddSingleton<IMousePathRecordingController>(provider => provider.GetRequiredService<MacroRecordingCoordinator>());
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var singleInstance = Services.GetRequiredService<ISingleInstanceService>();
            var backgroundLaunch = LaunchModeParser.IsBackgroundLaunch(
                Environment.GetCommandLineArgs());
            if (!singleInstance.TryAcquire())
            {
                if (!backgroundLaunch)
                    _ = singleInstance.SignalPrimary();
                ShutdownServices();
                Exit();
                return;
            }

            var mainWindow = Services.GetRequiredService<MainWindow>();
            singleInstance.ActivationRequested += (_, _) =>
                mainWindow.RequestShowFromSecondary();
            MainWindow = mainWindow;
            var settings = Services.GetRequiredService<SettingsViewModel>();
            mainWindow.Activate();
            mainWindow.ApplyLaunchMode(backgroundLaunch);
            await mainWindow.InitializeSystemSettingsAsync();
            await settings.CheckForUpdatesOnStartupAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("=== HyperKeyX 启动异常 ===");
            System.Diagnostics.Debug.WriteLine(ex.ToString());
            throw;
        }
    }

    internal static void ShutdownServices()
    {
        // 根容器负责释放协调器、钩子和进程监视器等平台服务。
        Interlocked.Exchange(ref _serviceProvider, null)?.Dispose();
    }
}
