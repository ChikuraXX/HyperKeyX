using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Domain.Configuration;

/// <summary>
/// 描述配置问题及其可导航的领域位置。
/// </summary>
public sealed record ConfigurationValidationIssue(
    ConfigurationValidationIssueCode Code,
    Guid? MacroId = null,
    Guid? ProfileId = null,
    InputTriggerIdentity Trigger = default,
    int StepIndex = -1,
    MacroValidationIssueCode? MacroIssueCode = null,
    PathIssue? PathIssue = null);
