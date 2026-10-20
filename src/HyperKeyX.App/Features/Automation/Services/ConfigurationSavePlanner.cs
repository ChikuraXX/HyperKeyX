using System.Collections.Frozen;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 从已发布文档和选定草稿构造可完整校验的原子保存候选。
/// </summary>
public sealed class ConfigurationSavePlanner
{
    public OperationResult<ConfigurationSavePlan> Plan(
        ConfigurationSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PublishedDocument);
        ArgumentNullException.ThrowIfNull(request.MacroChanges);
        ArgumentNullException.ThrowIfNull(request.TriggerChanges);

        if (HasDuplicateMacroChanges(request.MacroChanges))
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                "The save request contains duplicate macro changes.");
        }

        if (HasDuplicateTriggerChanges(request.TriggerChanges))
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                "The save request contains duplicate trigger changes.");
        }

        var deletedIds = request.MacroChanges
            .Where(change => change.Kind == MacroDraftChangeKind.Delete)
            .Select(change => change.MacroId)
            .ToHashSet();
        if (ReferencesAny(
                request.PublishedDocument,
                request.TriggerChanges,
                deletedIds))
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                "A referenced macro cannot be deleted.");
        }

        var candidate = request.PublishedDocument.DeepClone();
        var acceptedMacros = new HashSet<Guid>();
        var acceptedTriggers = new HashSet<TriggerDraftIdentity>();

        foreach (var change in request.MacroChanges)
        {
            var applied = ApplyMacroChange(candidate, change);
            if (!applied.Succeeded)
                return OperationResult<ConfigurationSavePlan>.Failure(applied.ErrorMessage!);
            acceptedMacros.Add(change.MacroId);
        }

        foreach (var snapshot in request.TriggerChanges.Where(
                     snapshot => snapshot.IsIncludedInSave))
        {
            var applied = ApplyTriggerSnapshot(candidate, snapshot);
            if (!applied.Succeeded)
                return OperationResult<ConfigurationSavePlan>.Failure(applied.ErrorMessage!);
            acceptedTriggers.Add(snapshot.Identity);
        }

        if (!AutomationConfigurationValidator.Validate(candidate).IsValid)
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                "The save candidate is invalid before normalization.");
        }

        candidate = Normalize(candidate);
        var validation = AutomationConfigurationValidator.Validate(candidate);
        if (!validation.IsValid)
        {
            var codes = string.Join(
                ", ",
                validation.Issues.Select(issue => issue.Code).Distinct());
            return OperationResult<ConfigurationSavePlan>.Failure(
                $"The normalized save candidate is invalid: {codes}.");
        }

        // 局部保存也必须持久化一份完整候选，避免宏库和触发器引用分开落盘。
        return OperationResult<ConfigurationSavePlan>.Success(new(
            candidate,
            acceptedMacros.ToFrozenSet(),
            acceptedTriggers.ToFrozenSet()));
    }

    private static bool HasDuplicateMacroChanges(
        IReadOnlyList<MacroDraftChange> changes) =>
        changes
            .GroupBy(change => change.MacroId)
            .Any(group => group.Count() > 1);

    private static bool HasDuplicateTriggerChanges(
        IReadOnlyList<TriggerDraftSnapshot> changes) =>
        changes
            .GroupBy(change => change.Identity)
            .Any(group => group.Count() > 1);

    private static bool ReferencesAny(
        AutomationConfigurationDocument document,
        IReadOnlyList<TriggerDraftSnapshot> triggerChanges,
        IReadOnlySet<Guid> macroIds)
    {
        if (macroIds.Count == 0)
            return false;

        var publishedReferences = (document.Profiles ?? [])
            .Where(profile => profile is not null)
            .SelectMany(profile =>
                (profile.KeyConfigs ?? [])
                    .Values
                    .Cast<TriggerBehaviorConfig?>()
                    .Concat((profile.MouseButtonConfigs ?? []).Values))
            .Where(config => config?.MacroId is Guid);
        return publishedReferences.Any(config =>
                   macroIds.Contains(config!.MacroId!.Value)) ||
               triggerChanges.Any(change =>
                   change.MacroId is Guid id && macroIds.Contains(id));
    }

    private static OperationResult ApplyMacroChange(
        AutomationConfigurationDocument candidate,
        MacroDraftChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var existingIndex = candidate.Macros.FindIndex(
            macro => macro.MacroId == change.MacroId);

        if (change.Kind == MacroDraftChangeKind.Delete)
        {
            if (existingIndex < 0)
            {
                return OperationResult.Failure(
                    "The macro selected for deletion does not exist.");
            }

            candidate.Macros.RemoveAt(existingIndex);
            return OperationResult.Success();
        }

        var macro = change.CreateMacroSnapshot();
        if (macro is null || macro.MacroId != change.MacroId)
            return OperationResult.Failure("The macro change is invalid.");

        macro.Name = (macro.Name ?? string.Empty).Trim();
        macro.Description = (macro.Description ?? string.Empty).Trim();
        if (existingIndex < 0)
            candidate.Macros.Add(macro);
        else
            candidate.Macros[existingIndex] = macro;
        return OperationResult.Success();
    }

    private static OperationResult ApplyTriggerSnapshot(
        AutomationConfigurationDocument candidate,
        TriggerDraftSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var profile = candidate.Profiles.FirstOrDefault(
            item => item.ProfileId == snapshot.ProfileId);
        if (profile is null)
            return OperationResult.Failure("The trigger profile does not exist.");

        var config = snapshot.CreateConfigSnapshot();
        switch (snapshot.Trigger.Kind, config)
        {
            case (InputTriggerKind.Keyboard, KeyConfig key):
                profile.KeyConfigs[snapshot.Trigger.Code] = key;
                return OperationResult.Success();

            case (InputTriggerKind.MouseButton, MouseButtonConfig mouse):
                profile.MouseButtonConfigs[(MouseButtonKind)snapshot.Trigger.Code] = mouse;
                return OperationResult.Success();

            default:
                return OperationResult.Failure(
                    "The trigger snapshot contains an unsupported value.");
        }
    }

    private static AutomationConfigurationDocument Normalize(
        AutomationConfigurationDocument document) => new()
    {
        SchemaVersion = document.SchemaVersion,
        Macros = document.Macros
            .Select(macro =>
            {
                var clone = macro.DeepClone();
                clone.Name = (clone.Name ?? string.Empty).Trim();
                clone.Description = (clone.Description ?? string.Empty).Trim();
                return clone;
            })
            .ToList(),
        Profiles = ProfileConfigurationNormalizer
            .NormalizeForDocumentStorage(document.Profiles)
    };
}
