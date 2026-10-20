namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 宏序列校验问题的稳定标识，由表现层负责本地化显示。
/// </summary>
public enum MacroValidationIssueCode
{
    TooManySteps,
    EnabledMacroHasNoSteps,
    UndefinedActionType,
    InvalidKeyboardKey,
    InvalidMouseButton,
    InvalidDuration,
    InvalidWheelDelta,
    DuplicateInputDown,
    InputUpWithoutDown,
    InputStillDownAtEnd,
    InvalidMousePath,
    InvalidStep,
    InvalidRandomDuration
}
