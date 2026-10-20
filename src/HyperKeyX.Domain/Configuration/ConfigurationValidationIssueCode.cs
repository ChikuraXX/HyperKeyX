namespace HyperKeyX.Domain.Configuration;

/// <summary>
/// 配置文档校验问题的稳定标识，由表现层负责本地化显示。
/// </summary>
public enum ConfigurationValidationIssueCode
{
    UnsupportedSchemaVersion,
    EmptyMacroId,
    DuplicateMacroId,
    EmptyMacroName,
    MacroNameTooLong,
    DuplicateMacroName,
    MacroDescriptionTooLong,
    InvalidMacroSequence,
    EmptyProfileId,
    DuplicateProfileId,
    EnabledMacroRequiresReference,
    EmptyMacroReference,
    MissingMacroReference,
    ConflictingAutomationModes,
    InvalidMousePath,
    TooManyPathPoints
}
