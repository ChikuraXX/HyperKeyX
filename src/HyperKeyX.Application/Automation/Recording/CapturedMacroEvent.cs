using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public enum CapturedMacroEventKind { Move, KeyDown, KeyUp, ButtonDown, ButtonUp, Wheel, ContextChanged, ExcludedBoundary }

public readonly record struct CapturedMacroEvent(long TimestampUs, CapturedMacroEventKind Kind,
    PathPosition? Position = null, KeyboardKeyIdentity? Key = null, MouseButtonKind Button = MouseButtonKind.None,
    int WheelDelta = 0, long Sequence = 0, MousePathRecordingContext? Context = null, long SourceSequence = 0);

public sealed record MousePathRecordingContext(PathPosition InitialPosition, MousePathCoordinateMode CoordinateMode,
    MousePathRecordedFrame RecordedFrame, MacroWindowTarget? Target = null,
    MousePathMode Mode = MousePathMode.Continuous, MousePathProcessingSettings? Processing = null,
    MousePathTimingSettings? Timing = null, MousePathRandomizationSettings? Randomization = null)
{
    public MousePathRecordingContext DeepClone() => this with
    {
        RecordedFrame = RecordedFrame with { }, Target = Target?.DeepClone(),
        Processing = Processing is null ? null : Processing with { },
        Timing = Timing is null ? null : Timing with { },
        Randomization = Randomization is null ? null : Randomization with { }
    };
}
