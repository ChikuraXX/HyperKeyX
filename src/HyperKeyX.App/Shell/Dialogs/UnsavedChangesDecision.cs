namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 用户针对未保存编辑工作区作出的退出决策。
/// </summary>
public enum UnsavedChangesDecision
{
    SaveAndExit,
    DiscardAndExit,
    Cancel
}
