using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 为离线导入的单个宏分配稳定且不会覆盖现有内容的身份和名称。
/// </summary>
public static class MacroImportConflictResolver
{
    public static MacroPackageImportResult Resolve(
        MacroDefinition imported,
        IReadOnlyCollection<MacroDefinition> existingMacros)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(existingMacros);

        var existing = existingMacros
            .Where(macro => macro is not null)
            .Select(Normalize)
            .ToList();
        var candidate = Normalize(imported);
        var sameIdentity = existing.FirstOrDefault(
            macro => macro.MacroId == candidate.MacroId);

        if (sameIdentity is not null && MacroContentComparer.Equals(candidate, sameIdentity))
        {
            return new MacroPackageImportResult(
                sameIdentity.DeepClone(),
                ReusedExisting: true,
                ExistingMacroId: sameIdentity.MacroId);
        }

        if (sameIdentity is not null)
        {
            var occupiedIds = existing
                .Select(macro => macro.MacroId)
                .ToHashSet();
            candidate.MacroId = CreateUniqueId(occupiedIds);
        }

        var occupiedNames = existing
            .Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        candidate.Name = AllocateName(candidate.Name, occupiedNames);

        return new MacroPackageImportResult(
            candidate.DeepClone(),
            ReusedExisting: false,
            ExistingMacroId: null);
    }

    private static MacroDefinition Normalize(MacroDefinition source)
    {
        var normalized = source.DeepClone();
        normalized.Name = (normalized.Name ?? string.Empty).Trim();
        normalized.Description = (normalized.Description ?? string.Empty).Trim();
        return normalized;
    }

    private static string AllocateName(
        string requestedName,
        ISet<string> occupiedNames)
    {
        if (!occupiedNames.Contains(requestedName))
            return requestedName;

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" ({suffix})";
            var rootLength = AutomationConfigurationValidator.MaximumNameLength -
                             suffixText.Length;
            var root = requestedName[..Math.Min(requestedName.Length, rootLength)];
            var candidate = root + suffixText;
            if (!occupiedNames.Contains(candidate))
                return candidate;
        }
    }

    private static Guid CreateUniqueId(IReadOnlySet<Guid> occupiedIds)
    {
        Guid candidate;
        do
        {
            candidate = Guid.NewGuid();
        }
        while (occupiedIds.Contains(candidate));

        return candidate;
    }
}
