using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 将稳定的领域校验代码转换为界面可读的双语文本。
/// </summary>
public static class MacroValidationMessageFormatter
{
    public static string Format(MacroValidationIssueCode code, bool isEnglish) =>
        (code, isEnglish) switch
        {
            (MacroValidationIssueCode.InvalidMousePath, false) => "轨迹数据或参数无效，请查看轨迹详情",
            (MacroValidationIssueCode.InvalidMousePath, true) => "Invalid path data or settings; open path details",
            (MacroValidationIssueCode.InvalidStep, false) => "宏步骤数据无效",
            (MacroValidationIssueCode.InvalidStep, true) => "The macro step data is invalid",
            (MacroValidationIssueCode.TooManySteps, false) => "宏步骤不能超过 512 步",
            (MacroValidationIssueCode.TooManySteps, true) => "A macro cannot exceed 512 steps",
            (MacroValidationIssueCode.EnabledMacroHasNoSteps, false) => "已启用的宏至少需要一个步骤",
            (MacroValidationIssueCode.EnabledMacroHasNoSteps, true) => "An enabled macro needs at least one step",
            (MacroValidationIssueCode.UndefinedActionType, false) => "宏动作类型无效",
            (MacroValidationIssueCode.UndefinedActionType, true) => "The macro action type is invalid",
            (MacroValidationIssueCode.InvalidKeyboardKey, false) => "键盘按键无效",
            (MacroValidationIssueCode.InvalidKeyboardKey, true) => "The keyboard key is invalid",
            (MacroValidationIssueCode.InvalidMouseButton, false) => "鼠标按键无效",
            (MacroValidationIssueCode.InvalidMouseButton, true) => "The mouse button is invalid",
            (MacroValidationIssueCode.InvalidDuration, false) => "时长必须在 0 到 60000 毫秒之间",
            (MacroValidationIssueCode.InvalidDuration, true) => "Duration must be between 0 and 60000 ms",
            (MacroValidationIssueCode.InvalidRandomDuration, false) => "随机延时需选择有效分布，且满足 0 ≤ 最小值 ≤ 最大值 ≤ 60000 毫秒；不能用于轨迹动作",
            (MacroValidationIssueCode.InvalidRandomDuration, true) => "Choose a valid distribution with 0 ≤ minimum ≤ maximum ≤ 60000 ms; random delay cannot be used on a path action",
            (MacroValidationIssueCode.InvalidWheelDelta, false) => "滚轮增量必须为非零且在 -1200 到 1200 之间",
            (MacroValidationIssueCode.InvalidWheelDelta, true) => "Wheel delta must be non-zero and between -1200 and 1200",
            (MacroValidationIssueCode.DuplicateInputDown, false) => "同一输入尚未弹起，不能再次按下",
            (MacroValidationIssueCode.DuplicateInputDown, true) => "This input is already down",
            (MacroValidationIssueCode.InputUpWithoutDown, false) => "弹起动作没有对应的按下动作",
            (MacroValidationIssueCode.InputUpWithoutDown, true) => "This input-up action has no matching input-down",
            (MacroValidationIssueCode.InputStillDownAtEnd, false) => "序列结束时输入仍处于按下状态",
            (MacroValidationIssueCode.InputStillDownAtEnd, true) => "This input is still down at the end of the sequence",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };

    public static string Format(PathIssue issue, bool isEnglish)
    {
        var (chinese, english) = issue.Code switch
        {
            PathIssueCode.InvalidSettings => ("轨迹参数无效，请检查时长、速度及误差范围", "Invalid path settings; check duration, speed and error limits"),
            PathIssueCode.InvalidCoordinate or PathIssueCode.UnreachablePosition => ("坐标超出有效区域或无法精确编码", "The position is outside the valid area or cannot be encoded exactly"),
            PathIssueCode.MissingTarget or PathIssueCode.InvalidTarget or PathIssueCode.DuplicateTarget => ("目标窗口定义缺失或无效", "The target window definition is missing or invalid"),
            PathIssueCode.AmbiguousTarget => ("匹配到多个目标窗口，请收紧匹配条件", "Multiple windows match; narrow the target criteria"),
            PathIssueCode.TargetUnavailable => ("目标窗口不可用或无法获得前台资格", "The target window is unavailable or cannot become foreground"),
            PathIssueCode.TargetChanged => ("窗口、显示器或指针状态已改变", "Window, display or pointer state changed"),
            PathIssueCode.TargetSwitchWhileHeld => ("持有按键或鼠标按钮时不能切换目标", "Cannot switch targets while input is held"),
            PathIssueCode.MissingGroup or PathIssueCode.InvalidGroup or PathIssueCode.DuplicateGroup => ("轨迹组或录制坐标框无效", "The path group or recorded coordinate frame is invalid"),
            PathIssueCode.ConflictingAnchor or PathIssueCode.DiscontinuousGroup or PathIssueCode.CrossedGroups => ("共享锚点或轨迹组顺序不连续", "Shared anchors or group ordering are discontinuous"),
            PathIssueCode.MotionLimitExceeded => ("最终轨迹无法满足运动或采样误差限制，请检查时序和误差参数", "The final path cannot meet motion or sampling error limits; check timing and error settings"),
            PathIssueCode.SchedulingOverrun => ("发送时间超出安全范围，播放已停止", "Scheduling exceeded the safe range; playback stopped"),
            PathIssueCode.RandomizationExhausted => ("16 次随机候选均未通过检查", "All 16 randomized candidates failed validation"),
            PathIssueCode.UnresolvedOutlier => ("源轨迹含无法解释的异常点，请修正源点", "The source contains an unresolved outlier; edit the source point"),
            PathIssueCode.TooManyPoints or PathIssueCode.CaptureStepLimitExceeded or PathIssueCode.CaptureDurationLimitExceeded => ("轨迹点数、步骤或时长超过容量限制", "Path points, steps or duration exceed capacity limits"),
            PathIssueCode.CaptureInterrupted or PathIssueCode.CaptureQueueOverflow => ("录制中断或队列溢出，请检查保留的草稿", "Recording was interrupted or overflowed; review the retained draft"),
            PathIssueCode.InputBusy => ("自动化输入正被其他会话使用", "Another session is using automation input"),
            PathIssueCode.InputSendFailed or PathIssueCode.InputReleaseFailed => ("输入发送或清理失败，播放已停止", "Input sending or cleanup failed; playback stopped"),
            PathIssueCode.InvalidEdit or PathIssueCode.InvalidAnchor or PathIssueCode.InvalidPoint or PathIssueCode.InvalidTimeOrder or PathIssueCode.DuplicatePoint => ("源点、锚点或编辑顺序无效", "A source point, anchor or edit ordering is invalid"),
            _ => ("轨迹数据结构无效", "The path data structure is invalid")
        };
        var prefix = issue.StepIndex >= 0 ? isEnglish ? $"Step {issue.StepIndex + 1}: " : $"步骤 {issue.StepIndex + 1}：" : string.Empty;
        return prefix + (isEnglish ? english : chinese);
    }
}
