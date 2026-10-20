using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Recording;

public sealed record MacroRecordingResult(
    MacroRecordingStopReason Reason,
    IReadOnlyList<MacroStep> Steps,
    string? ErrorMessage = null,
    MacroDefinition? Macro = null,
    IReadOnlyList<PathIssue>? PathIssues = null,
    IReadOnlyList<MacroValidationIssue>? SequenceIssues = null)
{
    public bool Succeeded => ErrorMessage is null;

    public bool CanApply => (Macro is not null && Reason is not (MacroRecordingStopReason.Cancelled or MacroRecordingStopReason.StartupFailed)) ||
        Succeeded && Reason is
        MacroRecordingStopReason.UserRequested or
        MacroRecordingStopReason.Shortcut or
        MacroRecordingStopReason.StepLimitReached;
}
