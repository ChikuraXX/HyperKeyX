using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 管理自动化运行期的配置快照、前台程序匹配和服务生命周期。
/// </summary>
public interface IAutomationRuntimeCoordinator
{
    event EventHandler<AutomationRuntimeContext>? ContextChanged;
    event EventHandler<string>? RuntimeError;

    bool IsRunning { get; }
    AutomationRuntimeContext CurrentContext { get; }

    void UpdateProfiles(IReadOnlyCollection<AppProfile> profiles);
    void Start();
    void Stop();
}
