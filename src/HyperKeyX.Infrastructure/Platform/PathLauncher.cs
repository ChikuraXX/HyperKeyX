using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class PathLauncher : IPathLauncher
{
    private readonly Func<ProcessStartInfo, bool> _startProcess;

    public PathLauncher()
        : this(startInfo => Process.Start(startInfo) is not null)
    {
    }

    internal PathLauncher(Func<ProcessStartInfo, bool> startProcess)
    {
        _startProcess = startProcess;
    }

    public OperationResult OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return OperationResult.Failure("目录路径为空。");

            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            var started = _startProcess(new ProcessStartInfo(fullPath)
            {
                UseShellExecute = true
            });
            return started
                ? OperationResult.Success()
                : OperationResult.Failure("无法打开目录。");
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            Win32Exception or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            // 平台错误以结果返回，设置页面负责显示本地化提示。
            return OperationResult.Failure(exception.Message);
        }
    }
}
