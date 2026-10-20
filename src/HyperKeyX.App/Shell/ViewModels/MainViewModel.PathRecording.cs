using HyperKeyX.Application.Automation.Recording;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Features.Automation.ViewModels;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private int _recordingKindIndex;
    private int _recordingCoordinateModeIndex = (int)MousePathCoordinateMode.VirtualDesktop;
    private Guid? _recordingTargetId;
    public int RecordingKindIndex
    {
        get => _recordingKindIndex;
        set { if (!IsRecordingSessionActive && value is >= 0 and <= 2 && SetProperty(ref _recordingKindIndex, value)) RaisePathRecordingOptions(); }
    }
    public int RecordingCoordinateModeIndex
    {
        get => _recordingCoordinateModeIndex;
        set { if (!IsRecordingSessionActive && value is >= 0 and <= 2 && SetProperty(ref _recordingCoordinateModeIndex, value)) RaisePathRecordingOptions(); }
    }
    public bool RecordingUsesPath => RecordingKindIndex != 0;
    public bool RecordingUsesWindow => RecordingUsesPath && RecordingCoordinateModeIndex == (int)MousePathCoordinateMode.WindowClient;
    public bool CanEditRecordingOptions => !IsRecordingSessionActive && !IsOrchestratorRunning && HasSelectedMacro;
    public IReadOnlyList<PathTargetChoice> RecordingTargets => MacroLibrary.SelectedMacro?.ToModel().WindowTargets
        .Select(t => new PathTargetChoice(t.TargetId, string.IsNullOrWhiteSpace(t.Name) ? t.ProcessName : t.Name)).ToArray() ?? [];
    public int RecordingTargetIndex
    {
        get => RecordingTargets.ToList().FindIndex(t => t.Id == _recordingTargetId);
        set
        {
            var targets = RecordingTargets;
            if (!IsRecordingSessionActive && value >= 0 && value < targets.Count && _recordingTargetId != targets[value].Id)
            { _recordingTargetId = targets[value].Id; OnPropertyChanged(); }
        }
    }
    public string RecordingOptionsText => L("录制选项", "Recording options");
    public string RecordingKindText => L("录制内容", "Capture mode");
    public string RecordingCoordinatesText => L("录制坐标", "Capture coordinates");
    public string RecordingTargetText => L("录制目标（先在轨迹详情中定义）", "Capture target (define it in path details first)");
    public string PathRecordingHelp => L(
        $"轨迹录制插入到所选步骤或其连续轨迹组之后。倒计时内将目标置于前台，并释放全部按键；{Settings.ActiveShortcuts.FinishRecording} 完成。进入应用控制区会中断轨迹。可分别录制多个目标，每次产生独立轨迹组。",
        $"Path capture inserts after the selected step or its continuous group. During countdown, foreground the target and release all inputs. {Settings.ActiveShortcuts.FinishRecording} finishes. Entering app controls interrupts capture. Record targets separately to create independent groups.");
    // 同一语言复用选项集合，避免坐标/模式变化触发 ItemsSource 重置并清空 ComboBox 选择。
    private static readonly IReadOnlyList<string> ChineseRecordingKinds = Array.AsReadOnly(new[]
        { "按键与点击（原模式）", "鼠标关键点", "鼠标连续轨迹" });
    private static readonly IReadOnlyList<string> EnglishRecordingKinds = Array.AsReadOnly(new[]
        { "Keys and clicks (legacy)", "Mouse key points", "Continuous mouse path" });
    private static readonly IReadOnlyList<string> ChineseRecordingCoordinates = Array.AsReadOnly(new[]
        { "相对起点", "虚拟桌面绝对坐标", "窗口客户区归一化" });
    private static readonly IReadOnlyList<string> EnglishRecordingCoordinates = Array.AsReadOnly(new[]
        { "Relative start", "Virtual desktop", "Normalized window client" });

    public IReadOnlyList<string> RecordingKindOptions => IsEnglish ? EnglishRecordingKinds : ChineseRecordingKinds;
    public IReadOnlyList<string> RecordingCoordinateOptions => IsEnglish ? EnglishRecordingCoordinates : ChineseRecordingCoordinates;

    private MousePathRecordingRequest? CreatePathRecordingRequest()
    {
        var macro = MacroLibrary.SelectedMacro?.ToModel();
        var coordinate = (MousePathCoordinateMode)RecordingCoordinateModeIndex;
        var target = coordinate == MousePathCoordinateMode.WindowClient
            ? macro?.WindowTargets.FirstOrDefault(t => t.TargetId == _recordingTargetId)?.DeepClone() : null;
        if (coordinate == MousePathCoordinateMode.WindowClient && target is null)
        { StatusText = L("请明确选择录制目标窗口。", "Select an explicit capture target window."); return null; }
        return new()
        {
            Mode = RecordingKindIndex == 1 ? MousePathMode.KeyPoints : MousePathMode.Continuous,
            CoordinateMode = coordinate, Target = target
        };
    }
    private void RaisePathRecordingOptions()
    {
        foreach (var name in new[] { nameof(RecordingUsesPath), nameof(RecordingUsesWindow), nameof(CanEditRecordingOptions),
            nameof(RecordingTargets), nameof(RecordingTargetIndex), nameof(RecordingOptionsText), nameof(RecordingKindText),
            nameof(RecordingCoordinatesText), nameof(RecordingTargetText), nameof(PathRecordingHelp),
            nameof(RecordingKindOptions), nameof(RecordingCoordinateOptions),
            // 语言切换会替换集合，最后重新同步有效索引；忽略控件清空期间的 -1 回写。
            nameof(RecordingKindIndex), nameof(RecordingCoordinateModeIndex) }) OnPropertyChanged(name);
    }
}
