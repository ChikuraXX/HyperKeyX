using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Application.Automation;
using HyperKeyX.Shell.Navigation;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private void InitializeProcessList()
    {
        var initialNames = _profiles
            .Select(profile => profile.TargetProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Concat(new[]
            {
                "notepad.exe", "calc.exe", "explorer.exe", "cmd.exe",
                "powershell.exe", "taskmgr.exe", "chrome.exe", "msedge.exe",
                "Code.exe", "devenv.exe"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        ProcessNames.Clear();
        foreach (var name in initialNames)
            ProcessNames.Add(name);
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(process.ProcessName))
                            names.Add(process.ProcessName + ".exe");
                    }
                    catch
                    {
                        // 进程可能在枚举期间退出，跳过该项即可。
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(SelectedProcessName))
                names.Add(SelectedProcessName);

            ProcessNames.Clear();
            foreach (var name in names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Take(300))
                ProcessNames.Add(name);

            StatusText = L(
                $"已刷新进程列表: {ProcessNames.Count} 个进程",
                $"Process list refreshed: {ProcessNames.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 刷新进程失败: {ex}");
            StatusText = L("刷新进程失败", "Failed to refresh processes");
        }
    }

    private void OnRuntimeContextChanged(
        object? sender,
        AutomationRuntimeContext context)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyRuntimeContext(context);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyRuntimeContext(context));
    }

    private void ApplyRuntimeContext(AutomationRuntimeContext context)
    {
        IsOrchestratorRunning = context.IsRunning;
        CurrentProcessName = context.IsProcessKnown
            ? context.ForegroundProcessName
            : L("未知", "Unknown");
        ActiveRuntimeProfileName = context.ActiveProfileDisplayName;
        WasEmergencyStopped = context.WasEmergencyStopped;

        StatusText = context.WasEmergencyStopped
            ? L("已紧急停止", "Emergency stop")
            : context.IsRunning
                ? L("运行中，连发和宏已激活", "Running, turbo and macros are active")
                : L("已停止", "Stopped");
    }

    private void OnRuntimeError(object? sender, string message)
    {
        _dispatcherQueue.TryEnqueue(() => StatusText = LocalizeRuntimeError(message));
    }

    [RelayCommand]
    private void ToggleOrchestrator()
    {
        if (RejectContextChangeWhileRecording())
        {
            return;
        }

        try
        {
            if (_runtimeCoordinator.IsRunning)
                _runtimeCoordinator.Stop();
            else
                _runtimeCoordinator.Start();
        }
        catch (Exception ex)
        {
            IsOrchestratorRunning = false;
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 启动服务失败: {ex}");
            StatusText = L("启动失败，请检查权限或系统钩子", "Startup failed; check permissions or the system hook");
        }
    }

    private void InitializeTriggerModes()
    {
        ReplaceOptions(TriggerModes, IsEnglish
            ? new[] { "Hold", "Toggle", "Long press" }
            : new[] { "按住触发", "开关切换", "长按触发" });
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        // 原位替换不会让 ComboBox 暂时失去选中项，避免 TwoWay 绑定回写 -1。
        while (target.Count < values.Count)
            target.Add(values[target.Count]);

        for (var index = 0; index < values.Count; index++)
        {
            if (!string.Equals(target[index], values[index], StringComparison.Ordinal))
                target[index] = values[index];
        }

        while (target.Count > values.Count)
            target.RemoveAt(target.Count - 1);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainNavigationState.ActiveSection))
            return;

        var recordingSection = _recordingOriginSection ??
            (_recordingOriginTrigger?.IsMouseTrigger == true
                ? MainSection.Mouse
                : MainSection.Keyboard);
        if (IsRecordingSessionActive && Navigation.ActiveSection != recordingSection)
        {
            Navigation.ActiveSection = recordingSection;
            StatusText = recordingSection == MainSection.Macros
                ? L(
                    "录制期间请留在宏编辑页面",
                    "Stay on the macro editor while recording")
                : L(
                    "录制期间请留在当前触发器页面",
                    "Stay on the current trigger page while recording");
            return;
        }

        Settings.RecordVisitedPage(MainNavigationState.ToPageId(Navigation.ActiveSection));
        if (Navigation.ActiveSection is MainSection.Keyboard or MainSection.Mouse)
        {
            // 非触发器页面不得重选宏；它们只保留最后一个键盘或鼠标上下文。
            ObserveSelectedTrigger(SelectedTriggerConfig);
            OnPropertyChanged(nameof(SelectedTriggerConfig));
            OnPropertyChanged(nameof(SelectedTriggerConfigText));
            OnPropertyChanged(nameof(EnableSelectedTurboText));
            OnPropertyChanged(nameof(SelectedPassThroughDescriptionText));
        }
        OnPropertyChanged(nameof(IsSettingsPageActive));
        RaiseTriggerDraftState();
        StatusText = GetNavigationStatusText();
    }
}
