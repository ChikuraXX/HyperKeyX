namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 未保存草稿退出提示的完整本地化文案。
/// </summary>
public sealed record UnsavedChangesPromptContent(
    string Title,
    string Message,
    string SaveAndExitText,
    string DiscardText,
    string CancelText);
