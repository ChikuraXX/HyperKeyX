using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Abstractions.Windows;

public sealed record WindowGeometry(PathPixelRectangle Client, uint Dpi, nint DpiAwarenessContext,
    bool IsVisible, bool IsMinimized, string DesktopLayout)
{
    public bool IsUsable => Client.IsValid && Dpi > 0 && DpiAwarenessContext != 0 && IsVisible && !IsMinimized;
}
