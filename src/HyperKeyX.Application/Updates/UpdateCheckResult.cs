namespace HyperKeyX.Application.Updates;

/// <summary>发布检查结果；失败状态不能当作已是最新版。</summary>
public enum UpdateCheckStatus
{
    /// <summary>存在比本地版本新的稳定版。</summary>
    UpdateAvailable,
    /// <summary>本地版本不低于远端稳定版。</summary>
    UpToDate,
    /// <summary>仓库尚无可访问的稳定版。</summary>
    NoRelease,
    /// <summary>GitHub 拒绝或限制了本次请求。</summary>
    RateLimited,
    /// <summary>网络、超时或服务器错误。</summary>
    NetworkError,
    /// <summary>发布元数据、版本号或链接无效。</summary>
    InvalidRelease,
    /// <summary>发现新版，但尚无符合发布约定的 x64 MSI 安装包。</summary>
    PackageUnavailable
}

/// <summary>检查结果；仅有效发布携带版本号和经校验的 HTTPS 发布页面。</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? Version = null, Uri? ReleasePage = null,
    Uri? InstallerDownload = null, string? InstallerSha256 = null, long InstallerBytes = 0);
