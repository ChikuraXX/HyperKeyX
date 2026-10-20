using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

public interface IRuntimeProfileCatalog
{
    event EventHandler? ProfilesReplaced;
    void ReplaceProfiles(IReadOnlyCollection<AppProfile> profiles);
    AppProfile? ResolveKnownProcess(string processName);
}
