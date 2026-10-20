using HyperKeyX.Application.Common;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Application.Abstractions.Updates;

/// <summary>准备独立安装辅助进程，等待当前应用退出后执行 MSI，不在应用运行中替换文件。</summary>
public interface IMsiUpdateInstaller
{
    /// <summary>成功表示辅助进程已就绪等待退出，并不表示 MSI 已安装成功；失败时调用方应保留当前应用。</summary>
    Task<OperationResult> StartAfterExitAsync(PreparedUpdate update, CancellationToken cancellationToken = default);
}
