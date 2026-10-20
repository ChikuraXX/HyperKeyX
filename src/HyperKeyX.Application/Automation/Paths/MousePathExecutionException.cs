using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed class MousePathExecutionException : Exception
{
    public MousePathExecutionException(IEnumerable<PathIssue> issues) : this(issues.ToArray()) { }
    private MousePathExecutionException(PathIssue[] issues) : base(string.Join(", ", issues.Select(i => i.Code))) =>
        Issues = Array.AsReadOnly(issues);
    public IReadOnlyList<PathIssue> Issues { get; }
}
