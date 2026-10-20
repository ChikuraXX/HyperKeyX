using HyperKeyX.Application.Updates;

namespace HyperKeyX.Application.Abstractions.Updates;

/// <summary>查询所选发布通道，不自动执行安装程序。</summary>
public interface IReleaseUpdateService
{
    /// <summary>与当前程序集版本比较；调用方取消时抛出取消异常。</summary>
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
    /// <summary>按完整语义版本检查；启用预览时同时比较稳定版和预览版，否则仅查询稳定版。</summary>
    Task<UpdateCheckResult> CheckAsync(string currentVersion, bool includePreviewUpdates, CancellationToken cancellationToken = default);
}
