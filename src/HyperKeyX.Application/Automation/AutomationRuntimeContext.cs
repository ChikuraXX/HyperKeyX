namespace HyperKeyX.Application.Automation;

/// <summary>
/// 表示当前自动化运行期使用的不可变上下文。
/// </summary>
public sealed record AutomationRuntimeContext(
    bool IsRunning,
    bool IsProcessKnown,
    bool WasEmergencyStopped,
    string ForegroundProcessName,
    Guid? ActiveProfileId,
    string ActiveProfileDisplayName)
{
    public static AutomationRuntimeContext Stopped { get; } =
        new(false, false, false, string.Empty, null, string.Empty);
}
