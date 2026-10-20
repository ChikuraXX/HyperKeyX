namespace HyperKeyX.Application.Abstractions.Persistence;

public interface IAppPaths
{
    string RootDirectory { get; }
    string ProfilesFilePath { get; }
    string PreferencesFilePath { get; }
    string AssetsDirectory { get; }
    string CacheDirectory { get; }
    string LogsDirectory { get; }
}
