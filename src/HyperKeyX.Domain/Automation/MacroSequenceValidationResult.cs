namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 表示宏序列的不可变校验结果。
/// </summary>
public sealed class MacroSequenceValidationResult
{
    public MacroSequenceValidationResult(
        IEnumerable<MacroValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
    }

    public IReadOnlyList<MacroValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
}
