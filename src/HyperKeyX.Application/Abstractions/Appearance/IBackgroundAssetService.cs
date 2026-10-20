using HyperKeyX.Application.Appearance;

namespace HyperKeyX.Application.Abstractions.Appearance;

public interface IBackgroundAssetService
{
    Task<BackgroundAssetImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    string? ResolvePath(string? assetName);

    bool Exists(string? assetName);
}
