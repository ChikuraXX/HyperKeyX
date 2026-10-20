using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Updates;

namespace HyperKeyX.Infrastructure.Updates;

/// <summary>启动独立的 MSI 安装辅助进程，并在当前应用退出前完成可信就绪握手。</summary>
public sealed class MsiUpdateInstaller : IMsiUpdateInstaller
{
    private const string ScriptResourceName = "HyperKeyX.Infrastructure.Updates.InstallMsiUpdate.ps1";
    private const int ParentExitTimeoutSeconds = 120;
    private const int MaximumEncodedCommandCharacters = 32_000;
    private static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<ProcessStartInfo, Process> _startProcess;
    private readonly Action<Process> _stopProcess;
    private readonly Func<string, string, Process, CancellationToken, Task<bool>> _waitForReady;
    private readonly Func<(int ProcessId, long StartTimeUtcTicks, string? ApplicationPath)> _getCurrentProcess;
    private readonly TimeSpan _readyTimeout;

    /// <summary>创建使用 Windows PowerShell 辅助进程的安装交接器。</summary>
    public MsiUpdateInstaller()
        : this(StartProcess, StopProcess, WaitForReadyAsync, GetCurrentProcess, DefaultReadyTimeout)
    {
    }

    /// <summary>为测试注入进程与握手边界，避免测试真正执行安装。</summary>
    internal MsiUpdateInstaller(
        Func<ProcessStartInfo, Process> startProcess,
        Action<Process> stopProcess,
        Func<string, string, Process, CancellationToken, Task<bool>> waitForReady,
        Func<(int ProcessId, long StartTimeUtcTicks, string? ApplicationPath)> getCurrentProcess,
        TimeSpan readyTimeout)
    {
        _startProcess = startProcess;
        _stopProcess = stopProcess;
        _waitForReady = waitForReady;
        _getCurrentProcess = getCurrentProcess;
        _readyTimeout = readyTimeout;
    }

    /// <inheritdoc />
    public async Task<OperationResult> StartAfterExitAsync(
        PreparedUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Failure("安装交接已取消，当前应用将继续运行。");
        }

        var validationError = ValidatePreparedUpdate(update);
        if (validationError is not null) return OperationResult.Failure(validationError);

        var (parentProcessId, parentStartTimeUtcTicks, applicationPath) = _getCurrentProcess();
        if (!IsTrustedApplicationPath(applicationPath))
        {
            return OperationResult.Failure("无法确认当前 HyperKeyX 可执行文件，已取消安装交接。");
        }

        var handoffId = Guid.NewGuid().ToString("N");
        var installerDirectory = Path.GetDirectoryName(update.InstallerPath)!;
        var readyPath = Path.Combine(installerDirectory, $".hyperkeyx-update-{handoffId}.ready");
        var resultPath = Path.Combine(installerDirectory, $".hyperkeyx-update-{handoffId}.result");
        var readyNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var payload = new InstallerPayload(
            update.InstallerPath,
            update.Sha256,
            update.Bytes,
            parentProcessId,
            parentStartTimeUtcTicks,
            applicationPath!,
            readyPath,
            readyNonce,
            resultPath,
            ParentExitTimeoutSeconds);

        Process? helper = null;
        try
        {
            helper = _startProcess(CreateStartInfo(payload));
            using var readyDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyDeadline.CancelAfter(_readyTimeout);
            var isReady = await _waitForReady(readyPath, readyNonce, helper, readyDeadline.Token)
                .ConfigureAwait(false);
            if (!isReady)
            {
                _stopProcess(helper);
                return OperationResult.Failure("安装辅助进程未能完成安全就绪握手，当前应用将继续运行。");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            if (helper is not null) _stopProcess(helper);
            return OperationResult.Failure(cancellationToken.IsCancellationRequested
                ? "安装交接已取消，当前应用将继续运行。"
                : "安装辅助进程就绪超时，当前应用将继续运行。");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (helper is not null) _stopProcess(helper);
            return OperationResult.Failure($"无法启动安装辅助进程：{exception.Message}");
        }
        finally
        {
            helper?.Dispose();
        }
    }

    /// <summary>校验仅用于交接边界的结构与文件元数据；安装前后哈希由独立辅助进程复核。</summary>
    private static string? ValidatePreparedUpdate(PreparedUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerPath) ||
            !Path.IsPathFullyQualified(update.InstallerPath) ||
            !string.Equals(Path.GetExtension(update.InstallerPath), ".msi", StringComparison.OrdinalIgnoreCase) ||
            !UpdatePackagePolicy.IsSha256(update.Sha256) ||
            update.Bytes <= 0 || update.Bytes > UpdatePackagePolicy.MaximumBytes)
        {
            return "安装包元数据无效，已取消安装交接。";
        }

        try
        {
            var file = new FileInfo(update.InstallerPath);
            return !file.Exists || file.Length != update.Bytes
                ? "安装包不存在或大小已变化，已取消安装交接。"
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"无法读取安装包：{exception.Message}";
        }
    }

    /// <summary>原应用重启路径只接受完整的 HyperKeyX.exe 文件路径。</summary>
    private static bool IsTrustedApplicationPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetFileName(path), "HyperKeyX.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    /// <summary>把 JSON 与完整脚本置于 UTF-16LE 编码命令中，避免路径进入 PowerShell 语法。</summary>
    private static ProcessStartInfo CreateStartInfo(InstallerPayload payload)
    {
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var script = ReadInstallerScript();
        var command = "$payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" +
                      payloadBase64 + "')) | ConvertFrom-Json\r\n" + script;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        if (encodedCommand.Length > MaximumEncodedCommandCharacters)
        {
            throw new InvalidOperationException("安装辅助命令超过 Windows 命令行长度限制。");
        }

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(encodedCommand);
        return info;
    }

    /// <summary>读取随基础设施程序集发布的可信安装脚本。</summary>
    private static string ReadInstallerScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException("程序集缺少 MSI 安装辅助脚本资源。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>等待辅助进程写入不可预测的随机数，并确认辅助进程仍在运行。</summary>
    private static async Task<bool> WaitForReadyAsync(
        string readyPath,
        string expectedNonce,
        Process helper,
        CancellationToken cancellationToken)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expectedNonce);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(readyPath))
                {
                    var actualBytes = await File.ReadAllBytesAsync(readyPath, cancellationToken).ConfigureAwait(false);
                    if (actualBytes.Length == expectedBytes.Length &&
                        CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
                    {
                        return !helper.HasExited;
                    }
                }
            }
            catch (IOException)
            {
                // 辅助进程可能仍在原子写入握手文件，下一轮重新读取。
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (helper.HasExited) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>启动实际的独立 PowerShell 辅助进程。</summary>
    private static Process StartProcess(ProcessStartInfo startInfo) =>
        Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell 辅助进程未启动。");

    /// <summary>握手失败时仅终止本次启动的辅助进程，不触碰当前应用或其他进程。</summary>
    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 进程已退出或句柄不可用时无需继续处理。
        }
    }

    /// <summary>捕获当前进程身份，供辅助进程防止 PID 复用。</summary>
    private static (int ProcessId, long StartTimeUtcTicks, string? ApplicationPath) GetCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        return (Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath);
    }

    /// <summary>传给辅助进程的受限安装交接参数。</summary>
    private sealed record InstallerPayload(
        string InstallerPath,
        string Sha256,
        long Bytes,
        int ParentProcessId,
        long ParentStartTimeUtcTicks,
        string ApplicationPath,
        string ReadyPath,
        string ReadyNonce,
        string ResultPath,
        int ParentExitTimeoutSeconds);
}
