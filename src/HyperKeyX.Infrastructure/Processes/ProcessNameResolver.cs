using System.Diagnostics;
using HyperKeyX.Application.Abstractions.Processes;

namespace HyperKeyX.Infrastructure.Processes;

/// <summary>
/// 将窗口进程 ID 转换为配置使用的可执行文件名。
/// </summary>
public sealed class ProcessNameResolver : IProcessNameResolver
{
    public bool TryGetProcessName(int processId, out string processName)
    {
        processName = string.Empty;
        if (processId <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            var value = process.ProcessName;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            processName = value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value.ToLowerInvariant()
                : value.ToLowerInvariant() + ".exe";
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessNameResolver] 读取进程名称失败: {ex}");
            return false;
        }
    }
}
