namespace HyperKeyX.Application.Automation.Recording;

public enum MacroRecordingStopReason
{
    UserRequested = 0,
    Shortcut = 1,
    StepLimitReached = 2,
    Cancelled = 3,
    StartupFailed = 4,
    QueueOverflow = 5,
    PointLimitReached = 6,
    DurationLimitReached = 7,
    ContextInterrupted = 8
}
