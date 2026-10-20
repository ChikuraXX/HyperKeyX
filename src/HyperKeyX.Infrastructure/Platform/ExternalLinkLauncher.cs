using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;

namespace HyperKeyX.Infrastructure.Platform;

/// <summary>通过 Windows Shell 启动默认浏览器，不执行非 HTTPS 协议。</summary>
public sealed class ExternalLinkLauncher : IExternalLinkLauncher
{
    private readonly Func<ProcessStartInfo, bool> _startProcess;

    /// <summary>使用系统 Shell 打开网页。</summary>
    public ExternalLinkLauncher() : this(info =>
    {
        using var process = Process.Start(info);
        // 已运行的浏览器可能通过 Shell 接收链接而不返回新进程句柄。
        return true;
    }) { }

    /// <summary>允许测试替换进程启动边界。</summary>
    internal ExternalLinkLauncher(Func<ProcessStartInfo, bool> startProcess) => _startProcess = startProcess;

    /// <inheritdoc />
    public OperationResult Open(Uri address)
    {
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
            return OperationResult.Failure("仅支持 HTTPS 网页。");
        try
        {
            return _startProcess(new(address.AbsoluteUri) { UseShellExecute = true })
                ? OperationResult.Success() : OperationResult.Failure("无法启动浏览器。");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or SecurityException)
        {
            return OperationResult.Failure(exception.Message);
        }
    }
}
