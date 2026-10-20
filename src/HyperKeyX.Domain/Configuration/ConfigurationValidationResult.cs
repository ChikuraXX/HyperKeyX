namespace HyperKeyX.Domain.Configuration;

public sealed class ConfigurationValidationResult
{
    public ConfigurationValidationResult(
        IEnumerable<ConfigurationValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
    }

    public IReadOnlyList<ConfigurationValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}
