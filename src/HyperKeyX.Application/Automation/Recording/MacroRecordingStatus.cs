namespace HyperKeyX.Application.Automation.Recording;

public sealed record MacroRecordingStatus(
    MacroRecordingState State,
    int CountdownSeconds,
    int CapturedStepCount,
    bool WaitingForRelease = false,
    long CapturedPointCount = 0);
