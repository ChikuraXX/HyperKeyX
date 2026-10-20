using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Automation;
using HyperKeyX.Application.Automation.Recording;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Keyboard.ViewModels;
using HyperKeyX.Features.Macros.Services;
using HyperKeyX.Features.Macros.ViewModels;
using HyperKeyX.Features.Mouse.ViewModels;
using HyperKeyX.Features.Profiles.ViewModels;
using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Shell.Dialogs;
using HyperKeyX.Shell.Dispatching;
using HyperKeyX.Shell.Navigation;
using Microsoft.UI.Xaml;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int DefaultKeyId = 0x1B;

    private readonly IConfigurationStore _configurationStore;
    private readonly IConfigurationTransferService _configurationTransferService;
    private readonly IMacroPackageTransferService _macroPackageTransferService;
    private readonly IConfigurationImportPrompt _configurationImportPrompt;
    private readonly IAutomationRuntimeCoordinator _runtimeCoordinator;
    private readonly ConfigurationSaveCoordinator _configurationSaveCoordinator;
    private readonly IMacroRecordingService _macroRecordingService;
    private readonly IUiDispatcher _dispatcherQueue;
    private readonly MacroReferenceIndex _macroReferenceIndex = new();
    private readonly List<PendingMacroBindingIntent> _pendingMacroBindingIntents = [];
    private readonly MacroEditorViewModel _unavailableMacroEditor = new();
    private readonly Dictionary<Guid, Dictionary<int, KeyConfigViewModel>> _profileEditors = new();
    private readonly Dictionary<int, KeyButtonStateViewModel> _keyboardKeyStates = new();
    private readonly Dictionary<int, KeyConfigViewModel> _unboundEditors = new();
    private readonly Dictionary<Guid, Dictionary<MouseButtonKind, MouseButtonConfigViewModel>> _mouseProfileEditors = new();
    private readonly Dictionary<MouseButtonKind, MouseButtonConfigViewModel> _unboundMouseEditors = new();
    private readonly Dictionary<MouseButtonKind, MouseButtonStateViewModel> _mouseButtonStates = new();

    private List<AppProfile> _profiles = new();
    private AutomationConfigurationDocument _publishedDocument = new();
    private PreparedConfigurationImport? _preparedConfigurationImport;
    private PendingConfigurationImport? _pendingConfigurationImport;
    private bool _hasPendingProfileWorkspaceChanges;
    private TriggerConfigViewModel? _observedSelectedTrigger;
    private MacroDefinitionViewModel? _recordingTargetMacro;
    private TriggerConfigViewModel? _recordingOriginTrigger;
    private MainSection? _recordingOriginSection;
    private bool _isApplyingProfileTarget;
    private bool _isUpdatingTriggerOptions;
    private MacroRecordingState _recordingState = MacroRecordingState.Idle;
    private int _recordingCountdownSeconds;
    private int _capturedStepCount;
    private bool _disposed;

    [ObservableProperty]
    private AppProfileViewModel? _selectedProfile;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isOrchestratorRunning;

    [ObservableProperty]
    private bool _wasEmergencyStopped;

    [ObservableProperty]
    private double _keyDownDuration;

    [ObservableProperty]
    private double _keyUpDuration;

    [ObservableProperty]
    private int _selectedTriggerIndex;

    [ObservableProperty]
    private string _currentProcessName = "None";

    [ObservableProperty]
    private string _activeRuntimeProfileName = string.Empty;

    [ObservableProperty]
    private KeyConfigViewModel _selectedKey = new(DefaultKeyId);

    [ObservableProperty]
    private MouseButtonConfigViewModel _selectedMouseButton = new(MouseButtonKind.Left);

    [ObservableProperty]
    private string _selectedProcessName = string.Empty;

    public ObservableCollection<AppProfileViewModel> Profiles { get; } = new();
    public ObservableCollection<string> ProcessNames { get; } = new();
    public ObservableCollection<string> TriggerModes { get; } = new();
    public ObservableCollection<MouseButtonStateViewModel> MouseButtons { get; } = new();
    public IRelayCommand<object?> SelectKeyCommand { get; }
    public IRelayCommand<object?> SelectMouseButtonCommand { get; }
    public IRelayCommand CreateMacroCommand { get; }
    public IRelayCommand CopyCurrentMacroCommand { get; }
    public IRelayCommand SaveCurrentMacroCommand { get; }
    public IRelayCommand RestoreCurrentMacroCommand { get; }
    public IRelayCommand OpenSelectedTriggerMacroCommand { get; }
    public IRelayCommand CopySelectedTriggerMacroCommand { get; }
    public IRelayCommand<MacroReferenceViewModel?> OpenMacroReferenceCommand { get; }
    public MainNavigationState Navigation { get; } = new();
    public SettingsViewModel Settings { get; }
    public MacroLibraryViewModel MacroLibrary { get; private set; }
    public MacroEditorViewModel SelectedMacroEditor =>
        MacroLibrary.SelectedMacro?.Editor ?? _unavailableMacroEditor;
    public bool HasSelectedMacro => MacroLibrary.SelectedMacro is not null;
    public bool CanEditSelectedMacro =>
        CanEditTriggerConfiguration && HasSelectedMacro;

    public int SelectedLanguageIndex
    {
        get => Settings.SelectedLanguageIndex;
        set => Settings.SelectedLanguageIndex = value;
    }

    public int SelectedThemeIndex
    {
        get => Settings.SelectedThemeIndex;
        set => Settings.SelectedThemeIndex = value;
    }

    public ElementTheme AppTheme => Settings.AppTheme;
    public bool IsEnglish => Settings.IsEnglish;
    public ObservableCollection<string> LanguageOptions => Settings.LanguageOptions;
    public ObservableCollection<string> ThemeOptions => Settings.ThemeOptions;

    public KeyConfigViewModel SelectedKeyConfig => SelectedKey;
    public TriggerConfigViewModel SelectedTriggerConfig =>
        Navigation.IsMouseActive
            ? SelectedMouseButton
            : Navigation.IsKeyboardActive
                ? SelectedKey
                : _observedSelectedTrigger ?? SelectedKey;
    public bool HasUnsavedChanges =>
        GetDirtyTriggerEditors().Count > 0 ||
        MacroLibrary.HasDirtyDrafts ||
        _pendingMacroBindingIntents.Count > 0 ||
        _hasPendingProfileWorkspaceChanges ||
        PendingConfigurationImport is not null;
    public AutomationConfigurationDocument PublishedDocument =>
        _publishedDocument.DeepClone();
    public IReadOnlyList<PendingMacroBindingIntent> PendingMacroBindingIntents =>
        _pendingMacroBindingIntents.ToArray();
    public ConfigurationImportSummary? PreparedConfigurationImportSummary =>
        _preparedConfigurationImport?.Summary;
    public PendingConfigurationImport? PendingConfigurationImport =>
        _pendingConfigurationImport;
    public bool CanSaveSelectedTriggerConfig =>
        PendingConfigurationImport is null &&
        !_hasPendingProfileWorkspaceChanges &&
        CanEditTriggerConfiguration &&
        SelectedTriggerConfig.CanSave;
    public MacroRecordingState RecordingState
    {
        get => _recordingState;
        private set
        {
            if (!SetProperty(ref _recordingState, value))
                return;

            RaiseRecordingStateChanged();
        }
    }

    public int RecordingCountdownSeconds
    {
        get => _recordingCountdownSeconds;
        private set
        {
            if (SetProperty(ref _recordingCountdownSeconds, value))
                OnPropertyChanged(nameof(RecordingIndicatorText));
        }
    }

    public int CapturedStepCount
    {
        get => _capturedStepCount;
        private set
        {
            if (SetProperty(ref _capturedStepCount, value))
                OnPropertyChanged(nameof(RecordingIndicatorText));
        }
    }

    public bool IsRecordingSessionActive =>
        RecordingState != MacroRecordingState.Idle;
    public bool CanEditKeyboardConfiguration => !IsRecordingSessionActive;
    public bool CanEditTriggerConfiguration => !IsRecordingSessionActive;
    public bool CanEditProfileWorkspace =>
        !IsRecordingSessionActive && PendingConfigurationImport is null;
    public bool CanToggleRecording =>
        IsRecordingSessionActive ||
        (!IsOrchestratorRunning && HasSelectedMacro);
    public string RecordIconGlyph =>
        IsRecordingSessionActive ? "\uE71A" : "\uE7C8";
    public bool IsSettingsPageActive => Navigation.IsSettingsActive;
    public string WindowTitleText => IsEnglish
        ? "HyperKeyX - Turbo & Macro Configuration"
        : "HyperKeyX - 按键连发 & 宏命令配置";
    public string SelectedProfileDisplayName => SelectedProfile?.DisplayName ?? "Global";
    public bool CanEditTargetProcess =>
        CanEditProfileWorkspace && SelectedProfile != null && !SelectedProfile.IsGlobal;
    public bool CanDeleteSelectedProfile =>
        CanEditProfileWorkspace && SelectedProfile != null && !SelectedProfile.IsGlobal;
    public string RunButtonText => IsEnglish
        ? (IsOrchestratorRunning ? "Stop" : "Start")
        : (IsOrchestratorRunning ? "停止" : "启动");
    public string RunIconGlyph => IsOrchestratorRunning ? "\uE71A" : "\uE768";
    public string KeyboardNavigationText => IsEnglish ? "Keyboard" : "键盘";
    public string MouseNavigationText => IsEnglish ? "Mouse" : "鼠标";
    public string MacrosNavigationText => IsEnglish ? "Macros" : "宏库";
    public string ProfilesText => IsEnglish ? "Profiles" : "配置文件";
    public string ProfileDetailsText => IsEnglish ? "Profile details" : "配置详情";
    public string ImportText => IsEnglish ? "Import" : "导入";
    public string ExportText => IsEnglish ? "Export" : "导出";
    public string SettingsTooltipText => IsEnglish ? "Settings" : "设置";
    public string AddProfileTooltipText => IsEnglish ? "Add profile" : "添加配置";
    public string DeleteProfileTooltipText => IsEnglish ? "Delete profile" : "删除配置";
    public string TargetText => IsEnglish ? "Target process" : "生效目标";
    public string ProcessPlaceholderText => SelectedProfile?.IsGlobal == true
        ? (IsEnglish ? "All processes (Global)" : "所有进程（Global）")
        : (IsEnglish ? "Select a process" : "选择进程");
    public string RefreshText => IsEnglish ? "Refresh" : "刷新";
    public string CurrentProcessText => IsEnglish ? "Foreground" : "前台进程";
    public string CurrentProcessDisplayText => $"{CurrentProcessText}: {CurrentProcessName}";
    public string ActiveRuntimeProfileText => IsEnglish ? "Active" : "当前生效";
    public string ActiveRuntimeProfileDisplayText =>
        $"{ActiveRuntimeProfileText}: " +
        (string.IsNullOrWhiteSpace(ActiveRuntimeProfileName)
            ? (IsEnglish ? "None" : "无")
            : ActiveRuntimeProfileName);
    public string KeyConfigText => IsEnglish ? "Key settings" : "按键配置";
    public string MouseConfigText => IsEnglish ? "Mouse settings" : "鼠标配置";
    public string SelectedTriggerConfigText =>
        SelectedTriggerConfig.IsMouseTrigger ? MouseConfigText : KeyConfigText;
    public string TurboText => IsEnglish ? "Turbo" : "连发";
    public string EnableTurboText => IsEnglish ? "Enable turbo" : "启用连发";
    public string EnableAutoClickText => IsEnglish ? "Enable auto-click" : "启用连点";
    public string EnableSelectedTurboText =>
        SelectedTriggerConfig.IsMouseTrigger ? EnableAutoClickText : EnableTurboText;
    public string KeyDownDurationText => IsEnglish ? "Key down duration" : "按下时长";
    public string KeyUpDurationText => IsEnglish ? "Release interval" : "弹起间隔";
    public string TriggerModeText => IsEnglish ? "Trigger mode" : "触发方式";
    public string LongPressThresholdText => IsEnglish ? "Long-press threshold" : "长按阈值";
    public string PassThroughOriginalInputText =>
        IsEnglish ? "Pass through original input" : "透传原始输入";
    public string PassThroughOriginalInputDescriptionText => IsEnglish
        ? "Also send the physical key to the target application"
        : "同时将物理按键发送给目标程序";
    public string MousePassThroughDescriptionText => IsEnglish
        ? "Also send the physical mouse button to the target application"
        : "同时将物理鼠标按键发送给目标程序";
    public string SelectedPassThroughDescriptionText =>
        SelectedTriggerConfig.IsMouseTrigger
            ? MousePassThroughDescriptionText
            : PassThroughOriginalInputDescriptionText;
    public string MacroText => IsEnglish ? "Macro" : "宏";
    public string MacroStepsText => IsEnglish ? "Macro steps" : "宏步骤";
    public string EnableMacroText => IsEnglish ? "Enable macro" : "启用宏";
    public string SelectMacroText => IsEnglish ? "Select a saved macro" : "选择已保存的宏";
    public string ClearMacroBindingText => IsEnglish ? "Unbind macro" : "取消绑定";
    public string OpenMacroText => IsEnglish ? "Open macro" : "打开宏";
    public string CopyMacroText => IsEnglish ? "Create a copy" : "创建副本";
    public string MissingMacroReferenceText => IsEnglish
        ? "The selected macro is missing or has not been saved."
        : "所选宏不存在或尚未保存。";
    public string RecordButtonText => IsEnglish
        ? (IsRecordingSessionActive ? "Stop recording" : "Start recording")
        : (IsRecordingSessionActive ? "停止录制" : "开始录制");
    public string RecordingIndicatorText => RecordingState switch
    {
        MacroRecordingState.Countdown => IsEnglish
            ? $"Recording starts in {RecordingCountdownSeconds}s"
            : $"将在 {RecordingCountdownSeconds} 秒后开始录制",
        MacroRecordingState.Recording => IsEnglish
            ? $"Recording... {CapturedStepCount} steps"
            : $"录制中... {CapturedStepCount} 步",
        MacroRecordingState.Stopping => IsEnglish
            ? "Stopping recording..."
            : "正在停止录制...",
        _ => string.Empty
    };
    public string NoMacroStepsText => IsEnglish ? "No macro steps" : "暂无宏步骤";
    public string SaveText => IsEnglish ? "Save key settings" : "保存按键配置";
    public string SaveTriggerText => IsEnglish ? "Save trigger settings" : "保存触发配置";
    public string UnsavedText => IsEnglish ? "Unsaved" : "未保存";
    public string SettingsTitleText => IsEnglish ? "Settings" : "设置";
    public string GlobalPreferencesText => IsEnglish ? "Global preferences" : "全局偏好";
    public string LanguageText => IsEnglish ? "Language" : "语言";
    public string ThemeText => IsEnglish ? "Theme" : "主题";
    public string MacroLibraryTitleText => IsEnglish ? "Macro library" : "宏库";
    public string MacroLibrarySubtitleText => IsEnglish
        ? "Reusable keyboard and mouse action sequences"
        : "可复用的键盘与鼠标动作序列";
    public string MacroSearchPlaceholderText => IsEnglish ? "Search macros" : "搜索宏";
    public string CreateMacroText => IsEnglish ? "New macro" : "新建宏";
    public string ImportMacroText => IsEnglish ? "Import macro" : "导入宏";
    public string ExportMacroText => IsEnglish ? "Export macro" : "导出宏";
    public string DeleteMacroText => IsEnglish ? "Delete macro" : "删除宏";
    public string SaveMacroText => IsEnglish ? "Save macro" : "保存宏";
    public string RestoreCurrentMacroText => IsEnglish
        ? "Restore saved version"
        : "恢复已保存版本";
    public string MacroNameText => IsEnglish ? "Name" : "名称";
    public string MacroDescriptionText => IsEnglish ? "Description" : "说明";
    public string MacroDescriptionPlaceholderText => IsEnglish
        ? "Optional notes about this macro"
        : "可选的宏用途说明";
    public string MacroReferencesText => IsEnglish ? "Used by" : "引用位置";
    public string NoMacroReferencesText => IsEnglish
        ? "This macro is not used by any trigger."
        : "当前没有触发器引用此宏。";
    public string NoMacroSelectedText => IsEnglish
        ? "Select a macro to view and edit it."
        : "请选择一个宏进行查看和编辑。";
    public string DeleteMacroTitleText => IsEnglish ? "Delete macro?" : "删除宏？";
    public string DeleteMacroConfirmationText => IsEnglish
        ? "The macro will be removed after the changes are saved."
        : "保存更改后，此宏将从宏库中移除。";
    public string DeleteText => IsEnglish ? "Delete" : "删除";
    public string CancelText => IsEnglish ? "Cancel" : "取消";
    public string BlockedMacroDeletionText => IsEnglish
        ? "This macro is still referenced. Open a reference before deleting it."
        : "此宏仍被触发器引用，请先打开并处理这些引用。";
    public string BackToMacroListText => IsEnglish ? "Back to macro list" : "返回宏列表";
    public string ExportDirtyMacroTitleText => IsEnglish
        ? "Export unsaved macro?"
        : "导出未保存的宏？";
    public string ExportDirtyMacroText => IsEnglish
        ? "Save the draft before exporting, or export the last saved version."
        : "可以先保存草稿再导出，或导出最近一次保存的版本。";
    public string SaveAndExportText => IsEnglish ? "Save and export" : "保存并导出";
    public string ExportSavedVersionText => IsEnglish
        ? "Export saved version"
        : "导出已保存版本";

    public MainViewModel(
        IConfigurationStore configurationStore,
        IConfigurationTransferService configurationTransferService,
        IMacroPackageTransferService macroPackageTransferService,
        IConfigurationImportPrompt configurationImportPrompt,
        SettingsViewModel settings,
        IAutomationRuntimeCoordinator runtimeCoordinator,
        IMacroRecordingService macroRecordingService,
        IUiDispatcher dispatcherQueue)
    {
        _configurationStore = configurationStore;
        _configurationTransferService = configurationTransferService;
        _macroPackageTransferService = macroPackageTransferService;
        _configurationImportPrompt = configurationImportPrompt;
        Settings = settings;
        _runtimeCoordinator = runtimeCoordinator;
        _configurationSaveCoordinator = new ConfigurationSaveCoordinator(
            _configurationStore,
            _runtimeCoordinator);
        _macroRecordingService = macroRecordingService;
        _dispatcherQueue = dispatcherQueue ??
            throw new ArgumentNullException(nameof(dispatcherQueue));

        // 必须先固定已发布基线，再创建宏和触发器草稿；加载结果不能隐式写回磁盘。
        var loaded = _configurationStore.Load();
        _publishedDocument = loaded.Document.DeepClone();
        _macroReferenceIndex.RebuildPublished(_publishedDocument, Settings.IsEnglish);
        MacroLibrary = new MacroLibraryViewModel(
            _publishedDocument.Macros,
            _macroReferenceIndex,
            Settings.IsEnglish);
        MacroLibrary.PropertyChanged += OnMacroLibraryPropertyChanged;
        MacroLibrary.Macros.CollectionChanged += OnMacroCollectionChanged;
        _unavailableMacroEditor.UpdateLocalization(Settings.IsEnglish);

        SelectKeyCommand = new RelayCommand<object?>(SelectKeyFromCommandParameter);
        SelectMouseButtonCommand = new RelayCommand<object?>(SelectMouseButtonFromCommandParameter);
        CreateMacroCommand = new RelayCommand(CreateMacroFromUi);
        CopyCurrentMacroCommand = new RelayCommand(CopyCurrentMacroFromUi);
        SaveCurrentMacroCommand = new RelayCommand(() => SaveCurrentMacro());
        RestoreCurrentMacroCommand = new RelayCommand(RestoreCurrentMacroFromUi);
        OpenSelectedTriggerMacroCommand = new RelayCommand(OpenSelectedTriggerMacroFromUi);
        CopySelectedTriggerMacroCommand = new RelayCommand(CopySelectedTriggerMacroFromUi);
        OpenMacroReferenceCommand = new RelayCommand<MacroReferenceViewModel?>(
            OpenMacroReferenceFromUi);
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Navigation.PropertyChanged += OnNavigationPropertyChanged;

        // 这些值在任何配置加载前就存在，NumberBox 首帧不会显示为空。
        this.KeyDownDuration = 100.0;
        this.KeyUpDuration = 100.0;
        this.SelectedKey = new KeyConfigViewModel(
            DefaultKeyId,
            publishedMacroChoices: MacroLibrary.PublishedChoices)
        {
            KeyDownDuration = 100.0,
            KeyUpDuration = 100.0,
            SelectedTriggerIndex = 0
        };
        this.SelectedMouseButton = new MouseButtonConfigViewModel(
            MouseButtonKind.Left,
            isEnglish: IsEnglish,
            publishedMacroChoices: MacroLibrary.PublishedChoices);

        SelectedKey.UpdateLocalization(IsEnglish);
        foreach (var button in Enum.GetValues<MouseButtonKind>()
                     .Where(button => InputTriggerIdentity.FromMouse(button).IsValid))
        {
            MouseButtons.Add(GetMouseButtonState(button));
        }
        CurrentProcessName = IsEnglish ? "None" : "无";
        InitializeTriggerModes();

        _macroRecordingService.StatusChanged += OnMacroRecordingStatusChanged;
        _macroRecordingService.Completed += OnMacroRecordingCompleted;
        _runtimeCoordinator.ContextChanged += OnRuntimeContextChanged;
        _runtimeCoordinator.RuntimeError += OnRuntimeError;

        LoadProfiles();
        var initialRuntime = ConfigurationRuntimeResolver.Resolve(_publishedDocument);
        if (initialRuntime.Succeeded)
        {
            // 运行时只接收已发布文档的深复制投影，绝不直接观察编辑器草稿。
            _runtimeCoordinator.UpdateProfiles(initialRuntime.Value);
        }
        else
        {
            Settings.ShowWarning(L(
                "配置无法用于运行时，自动化功能暂未发布",
                "The configuration could not be published to the runtime"));
        }

        var loadWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(loaded.WarningMessage))
            loadWarnings.Add(loaded.WarningMessage);
        if (loaded.WasMigrated)
        {
            loadWarnings.Add(L(
                "配置已从旧格式迁移，请检查后显式保存",
                "The configuration was migrated from a legacy format; review it before saving"));
        }

        // 迁移和备份恢复只建立内存草稿；提示用户后仍不得自动覆盖原文件。
        if (loadWarnings.Count > 0)
            Settings.ShowWarning(string.Join(Environment.NewLine, loadWarnings));
        Navigation.ActiveSection = MainNavigationState.FromPageId(Settings.ResolveInitialPage());
        InitializeProcessList();
        SelectKey(DefaultKeyId);
        SelectMouseButton(MouseButtonKind.Left);
        StatusText = IsEnglish ? "Ready" : "就绪";
    }

    partial void OnSelectedProfileChanged(AppProfileViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedProfileDisplayName));
        OnPropertyChanged(nameof(CanEditTargetProcess));
        OnPropertyChanged(nameof(CanDeleteSelectedProfile));
        OnPropertyChanged(nameof(ProcessPlaceholderText));

        if (value == null)
        {
            RaiseTriggerDraftState();
            return;
        }

        _isApplyingProfileTarget = true;
        SelectedProcessName = value.TargetProcessName;
        _isApplyingProfileTarget = false;

        var keyId = SelectedKey.KeyId > 0 ? SelectedKey.KeyId : DefaultKeyId;
        var mouseButton = SelectedMouseButton.Button;
        SelectedKey = GetOrCreateKeyEditor(keyId);
        SelectedMouseButton = GetOrCreateMouseButtonEditor(mouseButton);
        RefreshKeyHighlights();
        RefreshMouseButtonHighlights();
        RaiseTriggerDraftState();
        StatusText = L($"当前配置: {value.DisplayName}", $"Profile: {value.DisplayName}");
    }

    partial void OnSelectedKeyChanged(KeyConfigViewModel value)
    {
        value.UpdateLocalization(IsEnglish);

        if (Navigation.IsKeyboardActive)
            ObserveSelectedTrigger(value);

        UpdateKeyboardSelection(value.KeyId);
        RefreshKeyState(value.KeyId);
        RaiseSelectedKeyTextChanged();
        OnPropertyChanged(nameof(SelectedTriggerConfig));
        RaiseTriggerDraftState();
    }

    private void ObserveSelectedTrigger(TriggerConfigViewModel trigger)
    {
        if (!ReferenceEquals(_observedSelectedTrigger, trigger))
        {
            if (_observedSelectedTrigger is not null)
                _observedSelectedTrigger.PropertyChanged -= OnSelectedTriggerPropertyChanged;

            _observedSelectedTrigger = trigger;
            _observedSelectedTrigger.PropertyChanged += OnSelectedTriggerPropertyChanged;
        }

        KeyDownDuration = NormalizeDuration(trigger.KeyDownDuration);
        KeyUpDuration = NormalizeDuration(trigger.KeyUpDuration);
        SelectedTriggerIndex = Math.Clamp(trigger.SelectedTriggerIndex, 0, 2);
        RaiseTriggerDraftState();
    }

    partial void OnKeyDownDurationChanged(double value)
    {
        var normalized = NormalizeDuration(value);
        if (!DoubleEquals(value, normalized))
        {
            KeyDownDuration = normalized;
            return;
        }

        if (!DoubleEquals(SelectedTriggerConfig.KeyDownDuration, normalized))
            SelectedTriggerConfig.KeyDownDuration = normalized;
    }

    partial void OnKeyUpDurationChanged(double value)
    {
        var normalized = NormalizeDuration(value);
        if (!DoubleEquals(value, normalized))
        {
            KeyUpDuration = normalized;
            return;
        }

        if (!DoubleEquals(SelectedTriggerConfig.KeyUpDuration, normalized))
            SelectedTriggerConfig.KeyUpDuration = normalized;
    }

    partial void OnSelectedTriggerIndexChanged(int value)
    {
        if (_isUpdatingTriggerOptions)
            return;

        var normalized = Math.Clamp(value, 0, 2);
        if (value != normalized)
        {
            SelectedTriggerIndex = normalized;
            return;
        }

        if (SelectedTriggerConfig.SelectedTriggerIndex != normalized)
            SelectedTriggerConfig.SelectedTriggerIndex = normalized;
    }

    partial void OnCurrentProcessNameChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentProcessDisplayText));
    }

    partial void OnActiveRuntimeProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(ActiveRuntimeProfileDisplayText));
    }

    partial void OnSelectedProcessNameChanged(string value)
    {
        if (_isApplyingProfileTarget || SelectedProfile == null)
            return;

        if (PendingConfigurationImport is not null)
        {
            _isApplyingProfileTarget = true;
            SelectedProcessName = SelectedProfile.TargetProcessName;
            _isApplyingProfileTarget = false;
            StatusText = L(
                "完整配置替换待保存，不能单独修改生效目标",
                "Save or discard the pending replacement before changing its target");
            return;
        }

        if (IsRecordingSessionActive)
        {
            _isApplyingProfileTarget = true;
            SelectedProcessName = SelectedProfile.TargetProcessName;
            _isApplyingProfileTarget = false;
            StatusText = L(
                "录制期间不能更改生效目标",
                "The target process cannot be changed while recording");
            return;
        }

        if (!CanEditTargetProcess)
        {
            _isApplyingProfileTarget = true;
            SelectedProcessName = string.Empty;
            _isApplyingProfileTarget = false;
            return;
        }

        var normalized = NormalizeProcessName(value);
        var duplicateTarget = Profiles.Any(profile =>
            !ReferenceEquals(profile, SelectedProfile) &&
            string.Equals(profile.TargetProcessName, normalized, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(normalized) || duplicateTarget)
        {
            _isApplyingProfileTarget = true;
            SelectedProcessName = SelectedProfile.TargetProcessName;
            _isApplyingProfileTarget = false;
            StatusText = duplicateTarget
                ? L("该进程已绑定到其他配置", "That process is already assigned")
                : L("应用配置必须指定进程", "An application profile requires a process");
            return;
        }

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            _isApplyingProfileTarget = true;
            SelectedProcessName = normalized;
            _isApplyingProfileTarget = false;
        }

        SelectedProfile.TargetProcessName = normalized;
        _hasPendingProfileWorkspaceChanges = true;
        RaiseWorkspaceState();
        if (!ProcessNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            ProcessNames.Add(normalized);
        OnPropertyChanged(nameof(SelectedProfileDisplayName));
        StatusText = SaveAll()
            ? L("生效目标已保存", "Target process saved")
            : L("生效目标保存失败", "Failed to save target process");
    }

    partial void OnIsOrchestratorRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(RunIconGlyph));
        OnPropertyChanged(nameof(CanToggleRecording));
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        Navigation.ActiveSection = IsSettingsPageActive
            ? MainSection.Keyboard
            : MainSection.Settings;
    }

    private static string NormalizeProcessName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : Path.GetFileName(normalized);
    }

    private static double NormalizeDuration(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 100;

        return Math.Clamp(value, 10, 1000);
    }

    private static bool DoubleEquals(double left, double right)
    {
        return Math.Abs(left - right) < 0.001;
    }

    private bool RejectContextChangeWhileRecording()
    {
        if (!IsRecordingSessionActive)
            return false;

        StatusText = L(
            "请先停止当前宏录制",
            "Stop the current macro recording first");
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _macroRecordingService.StatusChanged -= OnMacroRecordingStatusChanged;
        _macroRecordingService.Completed -= OnMacroRecordingCompleted;
        _runtimeCoordinator.ContextChanged -= OnRuntimeContextChanged;
        _runtimeCoordinator.RuntimeError -= OnRuntimeError;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Navigation.PropertyChanged -= OnNavigationPropertyChanged;
        MacroLibrary.PropertyChanged -= OnMacroLibraryPropertyChanged;
        MacroLibrary.Macros.CollectionChanged -= OnMacroCollectionChanged;
        MacroLibrary.Dispose();
        if (_observedSelectedTrigger != null)
            _observedSelectedTrigger.PropertyChanged -= OnSelectedTriggerPropertyChanged;
        DisposeAllTriggerEditors();
        _runtimeCoordinator.Stop();
        _disposed = true;
    }
}
