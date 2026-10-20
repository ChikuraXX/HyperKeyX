namespace HyperKeyX.Domain.Automation.Paths;

public readonly record struct ProcessedPathPoint(
    Guid PointId, long OffsetUs, PathPosition Position, Guid? AnchorId,
    bool IsProtected, bool IsHoldBoundary = false);

public sealed class MousePathProcessingResult
{
    public MousePathProcessingResult(IEnumerable<ProcessedPathPoint> points,
        IEnumerable<PathIssue> issues, IEnumerable<Guid>? excludedPoints = null,
        IEnumerable<ProcessedPathPoint>? detailedPoints = null)
    {
        Points = Array.AsReadOnly(points.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        ExcludedPointIds = Array.AsReadOnly((excludedPoints ?? []).ToArray());
        DetailedPoints = detailedPoints is null ? Points : Array.AsReadOnly(detailedPoints.ToArray());
    }

    public IReadOnlyList<ProcessedPathPoint> Points { get; }
    public IReadOnlyList<PathIssue> Issues { get; }
    public IReadOnlyList<Guid> ExcludedPointIds { get; }
    public IReadOnlyList<ProcessedPathPoint> DetailedPoints { get; }
    public bool IsValid => Issues.Count == 0;
}
