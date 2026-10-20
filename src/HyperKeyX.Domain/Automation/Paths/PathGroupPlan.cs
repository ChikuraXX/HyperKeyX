namespace HyperKeyX.Domain.Automation.Paths;

public sealed record MousePathPlanningInput(MousePathProcessingResult Processed, MousePathDefinition Definition);

public sealed class PathGroupPlan
{
    internal PathGroupPlan(IEnumerable<PathPlan> plans, IEnumerable<PathIssue> issues)
    {
        Plans = Array.AsReadOnly(plans.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
    }
    public IReadOnlyList<PathPlan> Plans { get; }
    public IReadOnlyList<PathIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}
