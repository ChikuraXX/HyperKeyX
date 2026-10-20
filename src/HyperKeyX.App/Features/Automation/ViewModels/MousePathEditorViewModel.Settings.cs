namespace HyperKeyX.Features.Automation.ViewModels;

public sealed partial class MousePathEditorViewModel
{
    public double FixedDurationMs
    {
        get => (_path?.Timing.FixedDurationUs ?? 1000000) / 1_000d;
        set { if (!Equals(FixedDurationMs, value)) Change((p, g, _) => p.Timing.FixedDurationUs = ToUs(value)); }
    }
    public string FixedDurationMsLabel => L("固定总时长（毫秒）", "Fixed duration (ms)");
    public double Speed
    {
        get => (_path?.Timing.SpeedPxPerSecond ?? 1000);
        set { if (!Equals(Speed, value)) Change((p, g, _) => p.Timing.SpeedPxPerSecond = value); }
    }
    public string SpeedLabel => L("恒速目标（像素/秒）", "Cruise speed (px/s)");
    public double MaxSpeed
    {
        get => (_path?.Timing.MaxSpeedPxPerSecond ?? 10000);
        set { if (!Equals(MaxSpeed, value)) Change((p, g, _) => p.Timing.MaxSpeedPxPerSecond = value); }
    }
    public string MaxSpeedLabel => L("最大速度（像素/秒）", "Maximum speed (px/s)");
    public double MaxAcceleration
    {
        get => (_path?.Timing.MaxAccelerationPxPerSecondSquared ?? 100000);
        set { if (!Equals(MaxAcceleration, value)) Change((p, g, _) => p.Timing.MaxAccelerationPxPerSecondSquared = value); }
    }
    public string MaxAccelerationLabel => L("最大加速度（像素/秒²）", "Maximum acceleration (px/s²)");
    public double SamplingMs
    {
        get => (_path?.Processing.SamplingWindowUs ?? 8000) / 1_000d;
        set { if (!Equals(SamplingMs, value)) Change((p, g, _) => p.Processing.SamplingWindowUs = ToUs(value)); }
    }
    public string SamplingMsLabel => L("采样窗口（毫秒）", "Sampling window (ms)");
    public double MovementThreshold
    {
        get => (_path?.Processing.MovementThresholdPx ?? 2);
        set { if (!Equals(MovementThreshold, value)) Change((p, g, _) => p.Processing.MovementThresholdPx = value); }
    }
    public string MovementThresholdLabel => L("移动阈值（像素）", "Movement threshold (px)");
    public double StillnessMs
    {
        get => (_path?.Processing.StillnessDurationUs ?? 120000) / 1_000d;
        set { if (!Equals(StillnessMs, value)) Change((p, g, _) => p.Processing.StillnessDurationUs = ToUs(value)); }
    }
    public string StillnessMsLabel => L("静止确认（毫秒）", "Stillness confirmation (ms)");
    public bool SmoothingEnabled
    {
        get => (_path?.Processing.SmoothingEnabled ?? true);
        set { if (!Equals(SmoothingEnabled, value)) Change((p, g, _) => p.Processing.SmoothingEnabled = value); }
    }
    public string SmoothingEnabledLabel => L("启用平滑", "Enable smoothing");
    public double SmoothingDeviation
    {
        get => (_path?.Processing.SmoothingMaxDeviationPx ?? 2);
        set { if (!Equals(SmoothingDeviation, value)) Change((p, g, _) => p.Processing.SmoothingMaxDeviationPx = value); }
    }
    public string SmoothingDeviationLabel => L("平滑最大偏移（像素）", "Maximum smoothing deviation (px)");
    public bool SimplificationEnabled
    {
        get => (_path?.Processing.SimplificationEnabled ?? true);
        set { if (!Equals(SimplificationEnabled, value)) Change((p, g, _) => p.Processing.SimplificationEnabled = value); }
    }
    public string SimplificationEnabledLabel => L("启用简化", "Enable simplification");
    public double SimplificationDeviation
    {
        get => (_path?.Processing.SimplificationMaxDeviationPx ?? 1);
        set { if (!Equals(SimplificationDeviation, value)) Change((p, g, _) => p.Processing.SimplificationMaxDeviationPx = value); }
    }
    public string SimplificationDeviationLabel => L("简化空间误差（像素）", "Simplification spatial error (px)");
    public double SimplificationTimeMs
    {
        get => (_path?.Processing.SimplificationMaxTimeErrorUs ?? 2000) / 1_000d;
        set { if (!Equals(SimplificationTimeMs, value)) Change((p, g, _) => p.Processing.SimplificationMaxTimeErrorUs = ToUs(value)); }
    }
    public string SimplificationTimeMsLabel => L("简化时间误差（毫秒）", "Simplification time error (ms)");
    public bool TranslationEnabled
    {
        get => (_group?.Randomization.TranslationEnabled ?? false);
        set { if (!Equals(TranslationEnabled, value)) Change((p, g, _) => g.Randomization.TranslationEnabled = value); }
    }
    public string TranslationEnabledLabel => L("整条轨迹随机平移", "Random translation of the whole path");
    public double TranslationX
    {
        get => (_group?.Randomization.TranslationXPx ?? 3);
        set { if (!Equals(TranslationX, value)) Change((p, g, _) => g.Randomization.TranslationXPx = value); }
    }
    public string TranslationXLabel => L("X 平移范围（±像素）", "X translation range (±px)");
    public double TranslationY
    {
        get => (_group?.Randomization.TranslationYPx ?? 3);
        set { if (!Equals(TranslationY, value)) Change((p, g, _) => g.Randomization.TranslationYPx = value); }
    }
    public string TranslationYLabel => L("Y 平移范围（±像素）", "Y translation range (±px)");
    public bool JitterEnabled
    {
        get => (_group?.Randomization.JitterEnabled ?? false);
        set { if (!Equals(JitterEnabled, value)) Change((p, g, _) => g.Randomization.JitterEnabled = value); }
    }
    public string JitterEnabledLabel => L("局部平滑抖动", "Smooth local jitter");
    public double JitterAmplitude
    {
        get => (_group?.Randomization.JitterAmplitudePx ?? 1);
        set { if (!Equals(JitterAmplitude, value)) Change((p, g, _) => g.Randomization.JitterAmplitudePx = value); }
    }
    public string JitterAmplitudeLabel => L("抖动幅度（像素）", "Jitter amplitude (px)");
    public double JitterCorrelation
    {
        get => (_group?.Randomization.JitterCorrelationDistancePx ?? 64);
        set { if (!Equals(JitterCorrelation, value)) Change((p, g, _) => g.Randomization.JitterCorrelationDistancePx = value); }
    }
    public string JitterCorrelationLabel => L("抖动相关距离（像素）", "Jitter correlation distance (px)");
    public string TitleText => L("鼠标轨迹", "Mouse path");
    public string ModeLabel => L("轨迹模式", "Path mode");
    public string CoordinateLabel => L("坐标模式", "Coordinate mode");
    public string TimingLabel => L("时序模式", "Timing mode");
    public string ProcessingText => L("采样与处理", "Sampling and processing");
    public string RandomText => L("随机变化", "Random variation");
    public string TargetsText => L("目标窗口", "Target windows");
    public string PointsText => L("关键点", "Key points");
    public string PreviewText => L("数据预览", "Data preview");
    public string PreviewLegendText => L("灰线：原始轨迹；主题色：处理及随机示例。", "Gray: raw path. Accent: processed path and random example.");
    public string AddPointText => L("插入关键点", "Insert key point");
    public string DeletePointText => L("删除关键点", "Delete key point");
    public string RestoreSourceText => L("恢复原始轨迹", "Restore original path");
    public string PreviousText => L("上一页", "Previous");
    public string NextText => L("下一页", "Next");
    public string PointTimeLabel => L("时间（毫秒）", "Time (ms)");
    public string AddTargetText => L("添加目标", "Add target");
    public string RemoveTargetText => L("移除未引用目标", "Remove unused target");
    public string LocateErrorText => L("定位异常点", "Locate issue point");
    public string CoordinateHelp => L("点表按所选坐标模式解释；切换后请检查数值，原始点不会自动换算。", "Point values use the selected coordinate mode. Review values after switching; source points are not converted automatically.");
    public string TranslationHelp => L("整体平移会同时移动起点、终点和点击位置；局部抖动保留受保护锚点。", "Whole-path translation also moves endpoints and clicks. Local jitter preserves protected anchors.");
    public string RelativeHelp => L("相对轨迹每轮以当前位置为起点，位移可能逐轮累积。", "Relative paths start at the current pointer each cycle; displacement can accumulate.");
    public string PreviewHelp => L("仅用保存的坐标框显示抽样预览，不发送输入；当前桌面与窗口将在播放前检查。", "A sampled preview uses the saved coordinate frame and sends no input. Current desktop and windows are checked before playback.");
    public string RandomPreviewHelp => L("随机预览仅为示例，播放时每轮重新生成。", "Random preview is an example; each playback cycle generates a new result.");
    public string EntryDurationText => L("首点平滑定位另计，时长由播放时的位置确定。", "Smooth entry time is separate and depends on the pointer position at playback.");
    public string ProtectedPointText => L("锚点位置可编辑；共享边界同步修改，起点和终点不能删除。", "Anchor positions can be edited; shared boundaries update together. Start and end points cannot be deleted.");
    public IReadOnlyList<string> ModeOptions => [L("关键点", "Key points"), L("连续轨迹", "Continuous path")];
    public IReadOnlyList<string> CoordinateOptions => [L("相对起点", "Relative start"), L("虚拟桌面绝对坐标", "Virtual desktop"), L("窗口客户区归一化", "Normalized window client")];
    public IReadOnlyList<string> TimingOptions => [L("原始时间", "Original timing"), L("固定总时长", "Fixed duration"), L("恒定速度", "Constant speed")];
    public string RuntimeReadinessText => UsesWindow
        ? L("目标窗口将在播放前确定性匹配；保存时只验证结构。", "Target windows are matched deterministically before playback; saving validates structure.")
        : L("播放前检查当前桌面与起点。", "Current desktop and entry position are checked before playback.");
    public string DurationText => TrajectoryDurationMs is { } duration
        ? L($"轨迹时长：{duration:0.###} 毫秒", $"Path duration: {duration:0.###} ms")
        : L("轨迹时长：等待有效预览", "Path duration: awaiting a valid preview");
}
