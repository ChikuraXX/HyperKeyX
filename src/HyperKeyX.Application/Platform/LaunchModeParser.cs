namespace HyperKeyX.Application.Platform;

public static class LaunchModeParser
{
    private const string BackgroundArgument = "--background";

    public static bool IsBackgroundLaunch(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument => string.Equals(
            argument,
            BackgroundArgument,
            StringComparison.OrdinalIgnoreCase));
    }
}
