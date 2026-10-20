namespace HyperKeyX.Domain.Automation.Paths;

public sealed class PathValidationResult(IEnumerable<PathIssue> issues)
{
    public IReadOnlyList<PathIssue> Issues { get; } = Array.AsReadOnly(issues.ToArray());
    public bool IsValid => Issues.Count == 0;
}
