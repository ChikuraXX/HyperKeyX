using HyperKeyX.Application.Common;

namespace HyperKeyX.Application.Abstractions.Platform;

/// <summary>使用系统默认浏览器打开外部 HTTPS 页面。</summary>
public interface IExternalLinkLauncher
{
    /// <summary>打开绝对 HTTPS 地址；启动失败时返回失败结果。</summary>
    OperationResult Open(Uri address);
}
