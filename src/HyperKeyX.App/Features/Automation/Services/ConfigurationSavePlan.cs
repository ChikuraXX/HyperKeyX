using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Features.Automation.Services;

public sealed record ConfigurationSavePlan(
    AutomationConfigurationDocument Candidate,
    IReadOnlySet<Guid> AcceptedMacroIds,
    IReadOnlySet<TriggerDraftIdentity> AcceptedTriggerIds);
