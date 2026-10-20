using HyperKeyX.Application.Abstractions.Persistence;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class AppPaths : IAppPaths
{
    public AppPaths() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperKeyX"))
    {
    }

    public AppPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        ProfilesFilePath = Path.Combine(RootDirectory, "profiles.json");
        PreferencesFilePath = Path.Combine(RootDirectory, "preferences.json");
        AssetsDirectory = Path.Combine(RootDirectory, "Assets");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }
    public string ProfilesFilePath { get; }
    public string PreferencesFilePath { get; }
    public string AssetsDirectory { get; }
    public string CacheDirectory { get; }
    public string LogsDirectory { get; }
}
