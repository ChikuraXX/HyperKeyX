using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Macros.ViewModels;

public sealed record MacroReferenceViewModel(
    Guid ProfileId,
    string ProfileName,
    InputTriggerIdentity Trigger,
    string TriggerDisplayName,
    bool IsUnsavedDraft);
