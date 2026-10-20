using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Abstractions.Input;

public readonly record struct MouseInputEvent(
    MouseInputKind Kind,
    MouseButtonKind Button,
    int WheelDelta,
    int TargetProcessId,
    PathPosition? Position = null,
    long TimestampUs = 0,
    long Sequence = 0,
    bool IsInjected = false);
