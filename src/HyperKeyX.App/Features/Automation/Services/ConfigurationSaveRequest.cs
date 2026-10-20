using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Features.Automation.Services;

public sealed record ConfigurationSaveRequest(
    AutomationConfigurationDocument PublishedDocument,
    IReadOnlyList<MacroDraftChange> MacroChanges,
    IReadOnlyList<TriggerDraftSnapshot> TriggerChanges,
    ConfigurationWriteIntent WriteIntent = ConfigurationWriteIntent.Ordinary);
