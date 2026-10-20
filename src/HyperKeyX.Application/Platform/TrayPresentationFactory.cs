namespace HyperKeyX.Application.Platform;

public static class TrayPresentationFactory
{
    public static TrayPresentation Create(bool isEnglish, bool isRunning)
    {
        if (isEnglish)
        {
            return new TrayPresentation(
                isRunning ? "HyperKeyX - Running" : "HyperKeyX - Stopped",
                "Open HyperKeyX",
                isRunning ? "Stop" : "Start",
                "Exit");
        }

        return new TrayPresentation(
            isRunning ? "HyperKeyX - 已启动" : "HyperKeyX - 已停止",
            "打开 HyperKeyX",
            isRunning ? "停止" : "启动",
            "退出");
    }
}
