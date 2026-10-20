using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

internal sealed class MacroPathContextSnapshot
{
    public MacroPathContextSnapshot(IEnumerable<MousePathGroup> groups, IEnumerable<MacroWindowTarget> targets,
        IEnumerable<RecordedPathIssue>? recordingIssues = null)
    {
        Groups = Array.AsReadOnly(groups.Select(g => g.DeepClone()).ToArray());
        Targets = Array.AsReadOnly(targets.Select(t => t.DeepClone()).ToArray());
        RecordingIssues = Array.AsReadOnly((recordingIssues ?? []).ToArray());
    }
    internal IReadOnlyList<MousePathGroup> Groups { get; }
    internal IReadOnlyList<MacroWindowTarget> Targets { get; }
    internal IReadOnlyList<RecordedPathIssue> RecordingIssues { get; }
    public MacroDefinition ToModel() => new()
    {
        PathGroups = Groups.Select(g => g.DeepClone()).ToList(),
        WindowTargets = Targets.Select(t => t.DeepClone()).ToList()
    };
    public bool ContentEquals(MacroPathContextSnapshot other) =>
        RecordingIssues.SequenceEqual(other.RecordingIssues) && MacroContentComparer.Equals(ToModel(), other.ToModel());
}

internal sealed record RecordedPathIssue(Guid GroupId, PathIssue Issue);

internal sealed record MousePathEditTransaction(IReadOnlyList<MacroStepSnapshot> Rows,
    MacroPathContextSnapshot Context, Guid? Selection);
