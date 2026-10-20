using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 表示宏编辑器中的单个可编辑原始步骤。
/// </summary>
public sealed partial class MacroStepEditorViewModel : ObservableObject
{
    private bool _isEnglish;
    private MousePathDefinition? _mousePath;
    private MousePathGroup? _pathGroup;
    private string? _targetName;

    public MacroStepEditorViewModel(MacroStep step, bool isEnglish = false)
        : this(Guid.NewGuid(), step, isEnglish)
    {
    }

    internal MacroStepEditorViewModel(Guid stepId, MacroStep step, bool isEnglish)
    {
        if (stepId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(stepId));

        StepId = stepId;
        ReplaceWith(step);
        UpdateLocalization(isEnglish);
    }

    public Guid StepId { get; }
    public int SequenceNumber { get; private set; }
    public MacroActionType ActionType { get; private set; }
    public int VirtualKeyCode { get; private set; }
    public int ScanCode { get; private set; }
    public bool IsExtendedKey { get; private set; }
    public int DurationMs { get; private set; }
    /// <summary>空值为固定延时，否则与 DurationMs 构成随机区间。</summary>
    public int? RandomDurationMaxMs { get; private set; }
    public MacroDelayDistribution DelayDistribution { get; private set; }
    public bool IsRandomDelay => RandomDurationMaxMs.HasValue;
    public bool IsNormalDelay => IsRandomDelay && DelayDistribution == MacroDelayDistribution.Normal;
    public bool CanConfigureDelay => !IsPathAction;
    public string DurationRangeText => RandomDurationMaxMs is { } maximum
        ? $"{DurationMs}–{maximum}" : $"{DurationMs}";
    public string DelayCaption => IsRandomDelay
        ? L("最小延时（毫秒）", "Minimum delay (ms)") : L("延迟时长（毫秒）", "Delay duration (ms)");
    public string PostDelayCaption => IsRandomDelay
        ? L("最小延时（毫秒）", "Minimum delay (ms)") : L("动作后延迟（毫秒）", "Post-action delay (ms)");
    private string L(string chinese, string english) => _isEnglish ? english : chinese;
    public MouseButtonKind MouseButton { get; private set; }
    public int WheelDelta { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;
    public string AccessibleText { get; private set; } = string.Empty;
    public string DeleteButtonText { get; private set; } = string.Empty;
    public string ValidationText { get; private set; } = string.Empty;
    public bool HasValidationError => ValidationText.Length > 0;
    public bool IsKeyboardAction =>
        ActionType is MacroActionType.KeyDown or MacroActionType.KeyUp;
    public bool IsMouseButtonAction =>
        ActionType is MacroActionType.MouseButtonDown or MacroActionType.MouseButtonUp;
    public bool IsWheelAction => ActionType == MacroActionType.MouseWheel;
    public bool IsDelayAction => ActionType == MacroActionType.Delay;
    public bool IsPathAction => ActionType == MacroActionType.MousePath;
    public bool HasPostActionDelay => !IsDelayAction && !IsPathAction;

    public string IconGlyph => ActionType switch
    {
        MacroActionType.MouseButtonDown or MacroActionType.MouseButtonUp => "\uE962",
        MacroActionType.MouseWheel => WheelDelta >= 0 ? "\uE74A" : "\uE74B",
        MacroActionType.Delay => "\uE916",
        MacroActionType.MousePath => "\uE809",
        _ => "\uE765"
    };

    public MacroStep ToModel() => new()
    {
        ActionType = ActionType,
        VirtualKeyCode = VirtualKeyCode,
        ScanCode = ScanCode,
        IsExtendedKey = IsExtendedKey,
        DurationMs = DurationMs,
        RandomDurationMaxMs = RandomDurationMaxMs,
        DelayDistribution = DelayDistribution,
        MouseButton = MouseButton,
        WheelDelta = WheelDelta,
        MousePath = _mousePath?.DeepClone()
    };

    internal void ReplaceWith(MacroStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        // 所有字段必须一起替换，保证撤销、重做和持久化不会丢失原始输入信息。
        ActionType = step.ActionType;
        VirtualKeyCode = step.VirtualKeyCode;
        ScanCode = step.ScanCode;
        IsExtendedKey = step.IsExtendedKey;
        DurationMs = step.DurationMs;
        RandomDurationMaxMs = step.RandomDurationMaxMs;
        DelayDistribution = step.DelayDistribution;
        MouseButton = step.MouseButton;
        WheelDelta = step.WheelDelta;
        _mousePath = step.MousePath?.DeepClone();

        RaiseAllFields();
        RefreshText();
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        DeleteButtonText = isEnglish ? "Delete macro step" : "删除宏步骤";
        OnPropertyChanged(nameof(DeleteButtonText));
        RefreshText();
    }

    public void SetValidationIssues(
        IEnumerable<MacroValidationIssueCode> codes,
        bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(codes);

        ValidationText = string.Join(
            Environment.NewLine,
            codes
                .Distinct()
                .Select(code => MacroValidationMessageFormatter.Format(code, isEnglish)));
        OnPropertyChanged(nameof(ValidationText));
        OnPropertyChanged(nameof(HasValidationError));
        RefreshAccessibleText();
    }

    internal void SetSequenceNumber(int value)
    {
        if (SequenceNumber == value)
            return;

        SequenceNumber = value;
        OnPropertyChanged(nameof(SequenceNumber));
    }

    internal void SetPathContext(MousePathGroup? group, string? targetName)
    {
        _pathGroup = group?.DeepClone(); _targetName = targetName;
        if (IsPathAction) RefreshText();
    }

    private string CreatePathText()
    {
        string L(string zh, string en) => _isEnglish ? en : zh;
        if (_mousePath is not { } path) return L("鼠标轨迹：缺少数据", "Mouse path: missing data");
        var mode = path.Mode == MousePathMode.KeyPoints ? L("关键点", "Key points") : L("连续轨迹", "Continuous");
        var coordinates = _pathGroup?.CoordinateMode switch
        {
            MousePathCoordinateMode.RelativeStart => L("相对起点", "Relative start"),
            MousePathCoordinateMode.VirtualDesktop => L("虚拟桌面", "Virtual desktop"),
            MousePathCoordinateMode.WindowClient => L("客户区", "Window client") + " · " + _targetName,
            _ => L("缺少轨迹组", "Missing group")
        };
        var count = Math.Max(0, MousePathValidator.PointCost(path) - path.Edits.Count(e => e.IsDeleted));
        var duration = path.Timing.Mode switch
        {
            MousePathTimingMode.FixedDuration => $"{path.Timing.FixedDurationUs / 1_000d:0.###} ms",
            MousePathTimingMode.Original when path.Source.Points.Count > 0 =>
                $"{(path.Edits.FirstOrDefault(e => e.PointId == path.Source.Points[^1].PointId)?.OffsetUs ?? path.Source.Points[^1].OffsetUs) / 1_000d:0.###} ms",
            _ => L("恒速（详情中估时）", "Constant speed (estimate in details)")
        };
        var random = _pathGroup?.Randomization;
        var variation = string.Join(" + ", new[] { random?.TranslationEnabled == true ? L("平移", "Translation") : null,
            random?.JitterEnabled == true ? L("抖动", "Jitter") : null }.Where(s => s is not null));
        if (variation.Length == 0) variation = L("随机关闭", "Random off");
        return $"{L("鼠标轨迹", "Mouse path")} · {mode} · {coordinates} · {count} {L("点", "points")} · {duration} · {variation}";
    }

    private void RaiseAllFields()
    {
        OnPropertyChanged(nameof(ActionType));
        OnPropertyChanged(nameof(VirtualKeyCode));
        OnPropertyChanged(nameof(ScanCode));
        OnPropertyChanged(nameof(IsExtendedKey));
        OnPropertyChanged(nameof(DurationMs));
        OnPropertyChanged(nameof(RandomDurationMaxMs));
        OnPropertyChanged(nameof(IsRandomDelay));
        OnPropertyChanged(nameof(DelayDistribution));
        OnPropertyChanged(nameof(IsNormalDelay));
        OnPropertyChanged(nameof(CanConfigureDelay));
        OnPropertyChanged(nameof(DurationRangeText));
        OnPropertyChanged(nameof(MouseButton));
        OnPropertyChanged(nameof(WheelDelta));
        OnPropertyChanged(nameof(IsKeyboardAction));
        OnPropertyChanged(nameof(IsMouseButtonAction));
        OnPropertyChanged(nameof(IsWheelAction));
        OnPropertyChanged(nameof(IsDelayAction));
        OnPropertyChanged(nameof(IsPathAction));
        OnPropertyChanged(nameof(HasPostActionDelay));
        OnPropertyChanged(nameof(IconGlyph));
    }

    private void RefreshText()
    {
        OnPropertyChanged(nameof(DelayCaption));
        OnPropertyChanged(nameof(PostDelayCaption));
        DisplayText = ActionType switch
        {
            MacroActionType.MousePath => CreatePathText(),
            MacroActionType.Delay => _isEnglish
                ? $"Delay {DurationRangeText} ms"
                : $"延迟 {DurationRangeText} 毫秒",
            MacroActionType.MouseWheel => _isEnglish
                ? $"Wheel {(WheelDelta >= 0 ? "up" : "down")} {Math.Abs((long)WheelDelta)}"
                : $"滚轮{(WheelDelta >= 0 ? "向上" : "向下")} {Math.Abs((long)WheelDelta)}",
            MacroActionType.MouseButtonDown => _isEnglish
                ? $"{GetMouseButtonName()} down"
                : $"{GetMouseButtonName()}按下",
            MacroActionType.MouseButtonUp => _isEnglish
                ? $"{GetMouseButtonName()} up"
                : $"{GetMouseButtonName()}弹起",
            MacroActionType.KeyDown => CreateKeyboardText(_isEnglish ? "Key down" : "按下"),
            MacroActionType.KeyUp => CreateKeyboardText(_isEnglish ? "Key up" : "弹起"),
            _ => _isEnglish ? "Unknown macro action" : "未知宏动作"
        };

        if (HasPostActionDelay && RandomDurationMaxMs is not null)
            DisplayText += _isEnglish ? $" · delay {DurationRangeText} ms" : $" · 延迟 {DurationRangeText} 毫秒";
        if (IsRandomDelay)
            DisplayText += IsNormalDelay ? L("（正态）", " (normal)") : L("（均匀）", " (uniform)");
        OnPropertyChanged(nameof(DisplayText));
        RefreshAccessibleText();
    }

    private string CreateKeyboardText(string action)
    {
        var identity = KeyboardKeyIdentity.FromMacroStep(ToModel());
        return $"{action} {KeyboardKeyDisplayNameFormatter.GetDisplayName(identity, _isEnglish)}";
    }

    private string GetMouseButtonName() => MouseButton switch
    {
        MouseButtonKind.Left => _isEnglish ? "Left mouse button" : "鼠标左键",
        MouseButtonKind.Right => _isEnglish ? "Right mouse button" : "鼠标右键",
        MouseButtonKind.Middle => _isEnglish ? "Middle mouse button" : "鼠标中键",
        MouseButtonKind.XButton1 => _isEnglish ? "Mouse button 4" : "鼠标侧键 1",
        MouseButtonKind.XButton2 => _isEnglish ? "Mouse button 5" : "鼠标侧键 2",
        _ => _isEnglish ? "Invalid mouse button" : "无效鼠标按键"
    };

    private void RefreshAccessibleText()
    {
        AccessibleText = ValidationText.Length == 0
            ? DisplayText
            : $"{DisplayText}. {ValidationText}";
        OnPropertyChanged(nameof(AccessibleText));
    }
}
