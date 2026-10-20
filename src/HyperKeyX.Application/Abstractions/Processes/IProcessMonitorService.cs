namespace HyperKeyX.Application.Abstractions.Processes;

/// <summary>
/// 监控前台进程并触发切换事件的服务接口
/// </summary>
public interface IProcessMonitorService
{
    event EventHandler<string>? ForegroundProcessChanged;
    bool TryGetForegroundProcessName(out string processName);
    void Start();
    void Stop();
    bool IsRunning { get; }
}
