using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 基于活动配置协调连发和宏执行的服务接口
/// </summary>
public interface ITurboOrchestratorService
{
    event EventHandler<string>? RuntimeError;
    event EventHandler? EmergencyStopRequested;
    void SetActiveProfile(AppProfile? profile);
    void Start();
    void Stop();
    bool IsRunning { get; }
}
