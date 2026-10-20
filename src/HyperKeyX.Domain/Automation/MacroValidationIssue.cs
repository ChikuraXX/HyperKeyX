namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 表示一个宏校验问题及其对应的零基步骤索引。
/// </summary>
public sealed record MacroValidationIssue(
    MacroValidationIssueCode Code,
    int StepIndex);
