using HyperKeyX.Application.Platform;

namespace HyperKeyX.Shell.Notifications;

internal static class RuntimeNotificationFactory
{
    private const string RuntimeTag = "runtime-state";
    private const string RuntimeGroup = "runtime";

    public static LocalNotification? CreateToggleTransition(
        bool wasRunning,
        bool isRunning,
        bool isEnglish,
        string? activeProfileName)
    {
        // 快捷键未造成状态变化时不重复打扰用户。
        if (wasRunning == isRunning)
            return null;

        if (!isRunning)
        {
            return Create(
                isEnglish ? "HyperKeyX disabled" : "HyperKeyX 已关闭",
                isEnglish
                    ? "Automation stopped and held inputs were released."
                    : "自动化已停止，所有按住状态已释放。");
        }

        var profileName = activeProfileName?.Trim();
        if (string.IsNullOrEmpty(profileName))
        {
            return Create(
                isEnglish ? "HyperKeyX enabled" : "HyperKeyX 已启用",
                isEnglish
                    ? "Waiting for a matching profile."
                    : "正在等待匹配的配置。");
        }

        return Create(
            isEnglish ? "HyperKeyX enabled" : "HyperKeyX 已启用",
            isEnglish
                ? $"Active profile: {profileName}."
                : $"当前配置：{profileName}。");
    }

    public static LocalNotification CreateEmergencyStop(bool isEnglish) => Create(
        isEnglish ? "HyperKeyX emergency stop" : "HyperKeyX 已紧急停止",
        isEnglish
            ? "All automation stopped and held inputs were released."
            : "全部自动化已停止，所有按住状态已释放。");

    private static LocalNotification Create(string title, string message) =>
        new(title, message, RuntimeTag, RuntimeGroup);
}
