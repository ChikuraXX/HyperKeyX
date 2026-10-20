using HyperKeyX.Application.Common;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Application.Abstractions.Updates;

/// <summary>将官方安装包下载到应用缓存，严格核对大小与 SHA-256 后才返回可安装文件。</summary>
public interface IUpdatePackageDownloader
{
    /// <summary>支持进度与取消；取消时抛出取消异常并移除本次未完成文件。</summary>
    Task<OperationResult<PreparedUpdate>> DownloadAsync(UpdateCheckResult release,
        IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
