using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public enum RecordingInputDisposition { Record, StopControl, Excluded }

public interface IMousePathRecordingEnvironment
{
    bool AreAllInputsReleased();
    IMousePathRecordingContextLease CaptureContext(MousePathRecordingRequest request);
    /// <summary>只读取缓存的录制控制区域；此方法会在捕获回调内执行，禁止查询窗口或 UI。</summary>
    RecordingInputDisposition Classify(MouseInputEvent input);
}

public interface IMousePathRecordingContextLease : IDisposable
{
    MousePathRecordingContext Context { get; }
    PathIssue? Validate();
}

public sealed record MousePathRecordingRequest
{
    public MousePathCoordinateMode CoordinateMode { get; init; } = MousePathCoordinateMode.VirtualDesktop;
    public MousePathMode Mode { get; init; } = MousePathMode.Continuous;
    public MacroWindowTarget? Target { get; init; }
    public MousePathProcessingSettings Processing { get; init; } = new();
    public MousePathTimingSettings Timing { get; init; } = new();
    public MousePathRandomizationSettings Randomization { get; init; } = new();

    public MousePathRecordingRequest DeepClone() => this with
    {
        Target = Target?.DeepClone(), Processing = Processing with { }, Timing = Timing with { },
        Randomization = Randomization with { }
    };
}
