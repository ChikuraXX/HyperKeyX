namespace HyperKeyX.Domain.Automation.Paths;

public static class MousePathLimits
{
    public const int MaximumMacroPoints = 1_000_000;
    public const int MaximumDocumentPoints = 4_000_000;
    public const long MaximumRecordingDurationUs = 600_000_000;
    public const int CaptureQueueCapacity = 8_192;
    public const long MaximumPackageBytes = 256L * 1024 * 1024;
    public const long MaximumDocumentBytes = 512L * 1024 * 1024;
    public const long MaximumHistoryBytes = 256L * 1024 * 1024;
    public const int MaximumRandomCandidates = 16;
    public const long MaximumSchedulingLatenessUs = 50_000;
}
