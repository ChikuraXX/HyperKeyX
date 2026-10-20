using System.ComponentModel;
using System.Security.Principal;
using System.Text;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string SchedulerExecutable = "schtasks.exe";
    private const string TaskNamePrefix = "HyperKeyX.AutoStart.";
    private const int MissingTaskExitCode = 1;
    private const int MaximumErrorLength = 400;

    private readonly IProcessRunner _processRunner;
    private readonly string _executablePath;
    private readonly string _taskName;

    public StartupRegistrationService(IProcessRunner processRunner)
        : this(
            processRunner,
            Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前程序路径。"),
            WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName)
    {
    }

    internal StartupRegistrationService(
        IProcessRunner processRunner,
        string executablePath,
        string userScope)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userScope);
        if (executablePath.Contains('"', StringComparison.Ordinal))
            throw new ArgumentException("程序路径不能包含引号。", nameof(executablePath));

        _processRunner = processRunner;
        _executablePath = Path.GetFullPath(executablePath);
        _taskName = TaskNamePrefix + SanitizeScope(userScope);
    }

    public async Task<OperationResult<bool>> IsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var execution = await TryRunAsync(
            ["/Query", "/TN", _taskName],
            cancellationToken);
        if (!execution.Succeeded)
            return OperationResult<bool>.Failure(execution.ErrorMessage!);

        if (execution.Value.ExitCode == 0)
            return OperationResult<bool>.Success(true);

        // 退出码 1 也可能表示拒绝访问，只有明确的缺失信息才视为未启用。
        return IsMissingTaskResult(execution.Value)
            ? OperationResult<bool>.Success(false)
            : OperationResult<bool>.Failure(CreateFailureMessage(execution.Value));
    }

    public async Task<OperationResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var execution = await TryRunAsync(CreateMutationArguments(enabled), cancellationToken);
        if (!execution.Succeeded)
            return OperationResult.Failure(execution.ErrorMessage!);

        return execution.Value.ExitCode == 0
            ? OperationResult.Success()
            : OperationResult.Failure(CreateFailureMessage(execution.Value));
    }

    private IReadOnlyList<string> CreateMutationArguments(bool enabled)
    {
        if (!enabled)
            return ["/Delete", "/TN", _taskName, "/F"];

        return
        [
            "/Create",
            "/TN", _taskName,
            "/TR", $"\"{_executablePath}\" --background",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/IT",
            "/F"
        ];
    }

    private async Task<OperationResult<ProcessExecutionResult>> TryRunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                SchedulerExecutable,
                arguments,
                cancellationToken);
            return OperationResult<ProcessExecutionResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            Win32Exception or
            InvalidOperationException or
            NotSupportedException)
        {
            return OperationResult<ProcessExecutionResult>.Failure(
                NormalizeMessage(exception.Message));
        }
    }

    private static string CreateFailureMessage(ProcessExecutionResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(detail))
            detail = $"schtasks.exe 退出代码 {result.ExitCode}。";
        return NormalizeMessage(detail);
    }

    private static bool IsMissingTaskResult(ProcessExecutionResult result)
    {
        if (result.ExitCode != MissingTaskExitCode)
            return false;

        var detail = $"{result.StandardError} {result.StandardOutput}";
        return detail.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("cannot find the task", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("找不到指定的文件", StringComparison.Ordinal) ||
            detail.Contains("找不到文件", StringComparison.Ordinal) ||
            detail.Contains("找不到任务", StringComparison.Ordinal) ||
            detail.Contains("不存在", StringComparison.Ordinal);
    }

    private static string NormalizeMessage(string message)
    {
        var builder = new StringBuilder(Math.Min(message.Length, MaximumErrorLength));
        foreach (var part in message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
                builder.Append(' ');
            if (builder.Length + part.Length > MaximumErrorLength)
            {
                var remaining = MaximumErrorLength - builder.Length;
                if (remaining > 0)
                    builder.Append(part.AsSpan(0, Math.Min(remaining, part.Length)));
                break;
            }

            builder.Append(part);
        }

        return builder.Length == 0 ? "未知平台错误。" : builder.ToString();
    }

    private static string SanitizeScope(string scope)
    {
        var sanitized = string.Concat(scope.Select(character =>
            char.IsAsciiLetterOrDigit(character) ? character : '-'));
        return string.IsNullOrWhiteSpace(sanitized) ? "CurrentUser" : sanitized;
    }
}
