using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 保存已确认但尚未显式落盘的完整配置替换。
/// </summary>
public sealed class PendingConfigurationImport
{
    private readonly AutomationConfigurationDocument _candidate;
    private readonly AutomationConfigurationDocument _previousPublished;

    public PendingConfigurationImport(
        AutomationConfigurationDocument candidate,
        AutomationConfigurationDocument previousPublished,
        Guid? selectedProfileId,
        Guid? selectedMacroId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(previousPublished);
        _candidate = candidate.DeepClone();
        _previousPublished = previousPublished.DeepClone();
        SelectedProfileId = selectedProfileId;
        SelectedMacroId = selectedMacroId;
    }

    public AutomationConfigurationDocument Candidate => _candidate.DeepClone();
    public AutomationConfigurationDocument PreviousPublished =>
        _previousPublished.DeepClone();
    public Guid? SelectedProfileId { get; }
    public Guid? SelectedMacroId { get; }

    internal AutomationConfigurationDocument CreateCandidateSnapshot() =>
        _candidate.DeepClone();

    internal AutomationConfigurationDocument CreatePreviousSnapshot() =>
        _previousPublished.DeepClone();
}
