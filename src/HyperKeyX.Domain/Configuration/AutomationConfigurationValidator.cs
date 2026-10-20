using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Domain.Configuration;

/// <summary>
/// 在持久化与运行时发布前校验宏定义及触发器引用。
/// </summary>
public static class AutomationConfigurationValidator
{
    public const int MaximumNameLength = 100;
    public const int MaximumDescriptionLength = 500;

    public static ConfigurationValidationResult Validate(
        AutomationConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<ConfigurationValidationIssue>();
        if (document.SchemaVersion != AutomationConfigurationDocument.CurrentSchemaVersion)
        {
            issues.Add(new ConfigurationValidationIssue(
                ConfigurationValidationIssueCode.UnsupportedSchemaVersion));
        }

        var macroIds = ValidateMacros(document.Macros, issues);
        ValidateProfiles(document.Profiles, macroIds, issues);
        return new ConfigurationValidationResult(issues);
    }

    private static HashSet<Guid> ValidateMacros(
        IEnumerable<MacroDefinition?>? macros,
        ICollection<ConfigurationValidationIssue> issues)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long pointCount = 0;

        foreach (var macro in macros ?? [])
        {
            if (macro is null)
                continue;

            if (macro.MacroId == Guid.Empty)
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.EmptyMacroId,
                    MacroId: macro.MacroId));
            }
            else if (!ids.Add(macro.MacroId))
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.DuplicateMacroId,
                    MacroId: macro.MacroId));
            }

            var name = (macro.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.EmptyMacroName,
                    MacroId: macro.MacroId));
            }
            else
            {
                if (name.Length > MaximumNameLength)
                {
                    issues.Add(new ConfigurationValidationIssue(
                        ConfigurationValidationIssueCode.MacroNameTooLong,
                        MacroId: macro.MacroId));
                }

                if (!names.Add(name))
                {
                    issues.Add(new ConfigurationValidationIssue(
                        ConfigurationValidationIssueCode.DuplicateMacroName,
                        MacroId: macro.MacroId));
                }
            }

            if ((macro.Description ?? string.Empty).Trim().Length >
                MaximumDescriptionLength)
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.MacroDescriptionTooLong,
                    MacroId: macro.MacroId));
            }

            var sequence = MacroSequenceValidator.Validate(
                macro.Steps ?? [],
                isMacroEnabled: true);
            foreach (var pathIssue in MousePathValidator.Validate(macro).Issues)
                issues.Add(new(ConfigurationValidationIssueCode.InvalidMousePath,
                    MacroId: macro.MacroId, StepIndex: pathIssue.StepIndex, PathIssue: pathIssue));
            foreach (var step in macro.Steps ?? [])
                pointCount += MousePathValidator.PointCost(step?.MousePath);
            if (pointCount > MousePathLimits.MaximumDocumentPoints)
                issues.Add(new(ConfigurationValidationIssueCode.TooManyPathPoints, MacroId: macro.MacroId));
            foreach (var sequenceIssue in sequence.Issues)
            {
                // 保留底层错误码和原始行号，界面才能稳定定位第一个错误步骤。
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.InvalidMacroSequence,
                    MacroId: macro.MacroId,
                    StepIndex: sequenceIssue.StepIndex,
                    MacroIssueCode: sequenceIssue.Code));
            }
        }

        return ids;
    }

    private static void ValidateProfiles(
        IEnumerable<AppProfile?>? profiles,
        IReadOnlySet<Guid> macroIds,
        ICollection<ConfigurationValidationIssue> issues)
    {
        var profileIds = new HashSet<Guid>();
        foreach (var profile in profiles ?? [])
        {
            if (profile is null)
                continue;

            if (profile.ProfileId == Guid.Empty)
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.EmptyProfileId,
                    ProfileId: profile.ProfileId));
            }
            else if (!profileIds.Add(profile.ProfileId))
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.DuplicateProfileId,
                    ProfileId: profile.ProfileId));
            }

            foreach (var pair in (profile.KeyConfigs ?? [])
                         .OrderBy(pair => pair.Key))
            {
                if (pair.Value is null)
                    continue;

                ValidateTrigger(
                    pair.Value,
                    profile.ProfileId,
                    new InputTriggerIdentity(InputTriggerKind.Keyboard, pair.Key),
                    macroIds,
                    issues);
            }

            foreach (var pair in (profile.MouseButtonConfigs ?? [])
                         .OrderBy(pair => pair.Key))
            {
                if (pair.Value is null)
                    continue;

                ValidateTrigger(
                    pair.Value,
                    profile.ProfileId,
                    InputTriggerIdentity.FromMouse(pair.Key),
                    macroIds,
                    issues);
            }
        }
    }

    private static void ValidateTrigger(
        TriggerBehaviorConfig trigger,
        Guid profileId,
        InputTriggerIdentity identity,
        IReadOnlySet<Guid> macroIds,
        ICollection<ConfigurationValidationIssue> issues)
    {
        if (trigger.IsMacroEnabled && trigger.IsTurboEnabled)
        {
            issues.Add(new ConfigurationValidationIssue(
                ConfigurationValidationIssueCode.ConflictingAutomationModes,
                ProfileId: profileId,
                Trigger: identity));
        }

        if (trigger.MacroId is not Guid macroId)
        {
            if (trigger.IsMacroEnabled)
            {
                issues.Add(new ConfigurationValidationIssue(
                    ConfigurationValidationIssueCode.EnabledMacroRequiresReference,
                    ProfileId: profileId,
                    Trigger: identity));
            }

            return;
        }

        if (macroId == Guid.Empty)
        {
            issues.Add(new ConfigurationValidationIssue(
                ConfigurationValidationIssueCode.EmptyMacroReference,
                MacroId: macroId,
                ProfileId: profileId,
                Trigger: identity));
            return;
        }

        if (!macroIds.Contains(macroId))
        {
            issues.Add(new ConfigurationValidationIssue(
                ConfigurationValidationIssueCode.MissingMacroReference,
                MacroId: macroId,
                ProfileId: profileId,
                Trigger: identity));
        }
    }
}
