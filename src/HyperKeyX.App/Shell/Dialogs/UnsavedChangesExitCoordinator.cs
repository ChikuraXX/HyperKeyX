namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 将退出决策与 WinUI 窗口解耦，确保保存失败时不会继续关闭应用。
/// </summary>
public static class UnsavedChangesExitCoordinator
{
    public static async Task<bool> CanExitAsync(
        bool hasUnsavedChanges,
        Func<Task<UnsavedChangesDecision>> requestDecisionAsync,
        Func<bool> saveAllChanges)
    {
        ArgumentNullException.ThrowIfNull(requestDecisionAsync);
        ArgumentNullException.ThrowIfNull(saveAllChanges);

        if (!hasUnsavedChanges)
            return true;

        var decision = await requestDecisionAsync();
        return decision switch
        {
            // 只有完整保存事务成功，窗口才可继续关闭。
            UnsavedChangesDecision.SaveAndExit => saveAllChanges(),
            UnsavedChangesDecision.DiscardAndExit => true,
            _ => false
        };
    }
}
