using System.Globalization;

namespace HyperKeyX.Features.Settings.ViewModels;

public static class StorageSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        var safeBytes = Math.Max(0, bytes);
        var value = (double)safeBytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{safeBytes} {Units[unitIndex]}"
            : $"{value.ToString("0.00", CultureInfo.InvariantCulture)} {Units[unitIndex]}";
    }
}
