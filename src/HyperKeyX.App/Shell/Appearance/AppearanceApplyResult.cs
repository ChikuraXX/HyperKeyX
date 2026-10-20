namespace HyperKeyX.Shell.Appearance;

public readonly record struct AppearanceApplyResult(
    bool Succeeded,
    string? WarningMessage)
{
    public static AppearanceApplyResult Success() => new(true, null);

    public static AppearanceApplyResult Warning(string message) => new(false, message);
}
