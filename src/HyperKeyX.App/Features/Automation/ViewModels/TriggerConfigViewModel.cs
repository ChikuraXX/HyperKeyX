using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Macros.ViewModels;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 键盘与鼠标触发编辑器共用的行为、校验和宏草稿状态。
/// </summary>
public abstract partial class TriggerConfigViewModel : ObservableObject, IDisposable
{
    private bool _isLoading;
    private bool _disposed;
    private string _triggerDisplayName = string.Empty;
    private OrdinaryTriggerState _savedOrdinaryState;

    [ObservableProperty]
    private bool _isTurboEnabled;

    [ObservableProperty]
    private double _keyDownDuration = 100;

    [ObservableProperty]
    private double _keyUpDuration = 100;

    [ObservableProperty]
    private int _selectedTriggerIndex;

    [ObservableProperty]
    private double _longPressThresholdMs = 500;

    [ObservableProperty]
    private bool _passThroughOriginalInput;

    [ObservableProperty]
    private bool _isMacroEnabled;

    [ObservableProperty]
    private Guid? _selectedMacroId;

    protected TriggerConfigViewModel(
        InputTriggerIdentity triggerIdentity,
        ObservableCollection<MacroChoiceViewModel>? publishedMacroChoices = null)
    {
        if (!triggerIdentity.IsValid)
            throw new ArgumentOutOfRangeException(nameof(triggerIdentity));

        TriggerIdentity = triggerIdentity;
        PublishedMacroChoices = publishedMacroChoices ?? [];
        PublishedMacroChoices.CollectionChanged += OnPublishedMacroChoicesChanged;
    }

    public InputTriggerIdentity TriggerIdentity { get; }
    public string TriggerDisplayName
    {
        get => _triggerDisplayName;
        protected set => SetProperty(ref _triggerDisplayName, value);
    }

    public bool IsMouseTrigger => TriggerIdentity.Kind == InputTriggerKind.MouseButton;
    public ObservableCollection<MacroChoiceViewModel> PublishedMacroChoices { get; }
    /// <summary>包括失效引用，用户仍可主动解除绑定。</summary>
    public bool HasMacroSelection => SelectedMacroId.HasValue;
    public bool HasValidMacroSelection =>
        SelectedMacroId is Guid macroId &&
        macroId != Guid.Empty &&
        PublishedMacroChoices.Any(choice => choice.MacroId == macroId);
    public bool HasMissingEnabledMacro => IsMacroEnabled && !HasValidMacroSelection;
    public bool CanToggleMacro => IsMacroEnabled || HasValidMacroSelection;
    public bool CanSave => !IsMacroEnabled || HasValidMacroSelection;
    public bool IsDirty => !_savedOrdinaryState.Equals(CaptureOrdinaryState());
    protected bool IsEnglish { get; private set; }

    public abstract void UpdateLocalization(bool isEnglish);

    public void Dispose()
    {
        if (_disposed)
            return;

        PublishedMacroChoices.CollectionChanged -= OnPublishedMacroChoicesChanged;
        _disposed = true;
    }

    public void MarkSaved()
    {
        _savedOrdinaryState = CaptureOrdinaryState();
        RaiseDirtyState();
    }

    protected void LoadBehavior(TriggerBehaviorConfig config, bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(config);

        _isLoading = true;
        try
        {
            IsEnglish = isEnglish;
            IsTurboEnabled = config.IsTurboEnabled && !config.IsMacroEnabled;
            KeyDownDuration = NormalizeDuration(config.KeyDownDuration);
            KeyUpDuration = NormalizeDuration(config.KeyUpDuration);
            SelectedTriggerIndex = Math.Clamp(config.TriggerMode, 0, 2);
            LongPressThresholdMs = NormalizeLongPress(config.LongPressThresholdMs);
            PassThroughOriginalInput = config.PassThroughOriginalInput;
            IsMacroEnabled = config.IsMacroEnabled;
            SelectedMacroId = config.MacroId;
            _savedOrdinaryState = CaptureOrdinaryState();
        }
        finally
        {
            _isLoading = false;
        }

        RaiseDirtyState();
    }

    protected void WriteBehaviorTo(TriggerBehaviorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.IsTurboEnabled = IsTurboEnabled;
        config.KeyDownDuration = (int)NormalizeDuration(KeyDownDuration);
        config.KeyUpDuration = (int)NormalizeDuration(KeyUpDuration);
        config.TriggerMode = Math.Clamp(SelectedTriggerIndex, 0, 2);
        config.LongPressThresholdMs = (int)NormalizeLongPress(LongPressThresholdMs);
        config.PassThroughOriginalInput = PassThroughOriginalInput;
        config.IsMacroEnabled = IsMacroEnabled;
        config.MacroId = SelectedMacroId;
        // 触发器只持有共享宏引用；旧内联步骤不得再成为第二份可编辑状态。
        config.MacroSteps = [];
        config.RuntimeMacro = null;
    }

    protected void SetLanguage(bool isEnglish)
    {
        IsEnglish = isEnglish;
    }

    partial void OnIsTurboEnabledChanged(bool value)
    {
        if (_isLoading)
            return;

        // 同一触发器只允许一种输出策略，避免运行时优先级含糊。
        if (value && IsMacroEnabled)
            IsMacroEnabled = false;

        RaiseDirtyState();
    }

    partial void OnIsMacroEnabledChanged(bool value)
    {
        if (_isLoading)
            return;

        if (value && IsTurboEnabled)
            IsTurboEnabled = false;

        RaiseDirtyState();
    }

    partial void OnSelectedMacroIdChanged(Guid? value)
    {
        if (!_isLoading)
            RaiseDirtyState();
    }

    partial void OnKeyDownDurationChanged(double value) =>
        NormalizeProperty(value, NormalizeDuration, normalized => KeyDownDuration = normalized);

    partial void OnKeyUpDurationChanged(double value) =>
        NormalizeProperty(value, NormalizeDuration, normalized => KeyUpDuration = normalized);

    partial void OnLongPressThresholdMsChanged(double value) =>
        NormalizeProperty(
            value,
            NormalizeLongPress,
            normalized => LongPressThresholdMs = normalized);

    partial void OnSelectedTriggerIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 2);
        if (value != normalized)
        {
            SelectedTriggerIndex = normalized;
            return;
        }

        if (!_isLoading)
            RaiseDirtyState();
    }

    partial void OnPassThroughOriginalInputChanged(bool value)
    {
        if (!_isLoading)
            RaiseDirtyState();
    }

    private void NormalizeProperty(
        double value,
        Func<double, double> normalize,
        Action<double> assign)
    {
        var normalized = normalize(value);
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            Math.Abs(value - normalized) >= 0.001)
        {
            assign(normalized);
            return;
        }

        if (!_isLoading)
            RaiseDirtyState();
    }

    private void OnPublishedMacroChoicesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasMacroSelection));
        OnPropertyChanged(nameof(HasValidMacroSelection));
        OnPropertyChanged(nameof(HasMissingEnabledMacro));
        OnPropertyChanged(nameof(CanToggleMacro));
        OnPropertyChanged(nameof(CanSave));
    }

    private void RaiseDirtyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasMacroSelection));
        OnPropertyChanged(nameof(HasValidMacroSelection));
        OnPropertyChanged(nameof(HasMissingEnabledMacro));
        OnPropertyChanged(nameof(CanToggleMacro));
    }

    private OrdinaryTriggerState CaptureOrdinaryState() => new(
        IsTurboEnabled,
        NormalizeDuration(KeyDownDuration),
        NormalizeDuration(KeyUpDuration),
        Math.Clamp(SelectedTriggerIndex, 0, 2),
        NormalizeLongPress(LongPressThresholdMs),
        PassThroughOriginalInput,
        IsMacroEnabled,
        SelectedMacroId);

    private static double NormalizeDuration(double value) =>
        NormalizeFinite(value, minimum: 10, maximum: 1_000, fallback: 100);

    private static double NormalizeLongPress(double value) =>
        NormalizeFinite(value, minimum: 250, maximum: 2_000, fallback: 500);

    private static double NormalizeFinite(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;

        return Math.Clamp(value, minimum, maximum);
    }

    private readonly record struct OrdinaryTriggerState(
        bool IsTurboEnabled,
        double KeyDownDuration,
        double KeyUpDuration,
        int SelectedTriggerIndex,
        double LongPressThresholdMs,
        bool PassThroughOriginalInput,
        bool IsMacroEnabled,
        Guid? SelectedMacroId);
}
