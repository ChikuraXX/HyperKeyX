using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Automation;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 协调配置候选的校验、持久化和运行时发布。
/// </summary>
public sealed class ConfigurationSaveCoordinator
{
    private readonly IConfigurationStore _store;
    private readonly IAutomationRuntimeCoordinator _runtimeCoordinator;
    private readonly ConfigurationSavePlanner _planner;
    private readonly Func<
        AutomationConfigurationDocument,
        OperationResult<IReadOnlyList<AppProfile>>> _runtimeResolver;

    public ConfigurationSaveCoordinator(
        IConfigurationStore store,
        IAutomationRuntimeCoordinator runtimeCoordinator,
        ConfigurationSavePlanner? planner = null,
        Func<
            AutomationConfigurationDocument,
            OperationResult<IReadOnlyList<AppProfile>>>? runtimeResolver = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runtimeCoordinator = runtimeCoordinator ??
            throw new ArgumentNullException(nameof(runtimeCoordinator));
        _planner = planner ?? new ConfigurationSavePlanner();
        _runtimeResolver = runtimeResolver ?? ConfigurationRuntimeResolver.Resolve;
    }

    public OperationResult<ConfigurationSavePlan> Save(
        ConfigurationSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planned = _planner.Plan(request);
        if (!planned.Succeeded)
            return planned;

        // 先生成深拷贝运行时投影，确保写盘期间的对象变化不会污染即将发布的快照。
        var runtime = _runtimeResolver(planned.Value.Candidate);
        if (!runtime.Succeeded)
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                runtime.ErrorMessage ?? "The runtime projection could not be resolved.");
        }

        // 只有完整候选成功落盘后才发布运行时；失败时编辑器仍保留原基线和撤销历史。
        var persisted = _store.Save(
            planned.Value.Candidate,
            request.WriteIntent);
        if (!persisted.Succeeded)
        {
            return OperationResult<ConfigurationSavePlan>.Failure(
                persisted.ErrorMessage ?? "The configuration could not be persisted.");
        }

        _runtimeCoordinator.UpdateProfiles(runtime.Value);
        return planned;
    }
}
