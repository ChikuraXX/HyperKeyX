namespace HyperKeyX.Application.Appearance;

public readonly record struct BackgroundAssetImportResult(
    bool Succeeded,
    string? AssetName,
    string? ErrorMessage)
{
    public static BackgroundAssetImportResult Success(string assetName) =>
        new(true, assetName, null);

    public static BackgroundAssetImportResult Failure(string message) =>
        new(false, null, message);
}
