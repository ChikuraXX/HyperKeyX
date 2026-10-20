namespace HyperKeyX.Application.Automation.Recording;

internal sealed record MacroRecordingOptions(
    int CountdownSeconds = 3,
    int MaxSteps = 512,
    int MaxPathPoints = 1_000_000,
    long MaxPathDurationUs = 600_000_000,
    int CaptureQueueCapacity = 8_192);
