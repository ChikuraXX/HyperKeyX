using System.ComponentModel;
using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new Win32Exception($"无法启动进程：{fileName}");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 只终止本服务创建的子进程，避免取消后遗留后台命令。
            TryTerminate(process);
            throw;
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 进程已在状态检查后退出，无需继续处理。
        }
        catch (Win32Exception)
        {
            // 取消路径不使用终止失败覆盖原始取消异常。
        }
    }
}
