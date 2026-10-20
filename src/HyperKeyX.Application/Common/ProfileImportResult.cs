using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Common;

public sealed record ProfileImportResult(
    IReadOnlyList<AppProfile> Profiles,
    string? ValidationError);
