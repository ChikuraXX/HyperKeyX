namespace HyperKeyX.Application.Platform;

public sealed record LocalNotification(
    string Title,
    string Message,
    string Tag,
    string Group);
