namespace HyperKeyX.Application.Abstractions.Windows;

public sealed record WindowCandidate(nint Handle, int ProcessId, long ProcessStartUtcTicks,
    string ProcessName, string? ExecutablePath, string WindowClass, string Title, bool IsVisible);
