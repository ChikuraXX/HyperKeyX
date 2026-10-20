using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Application.Abstractions.Persistence;

public interface IAppPreferencesStore
{
    AppPreferences Load();
    bool Save(AppPreferences preferences);
    string PreferencesFilePath { get; }
}
