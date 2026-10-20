using Microsoft.UI.Xaml;

namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 抽象真正退出时的未保存草稿决策提示。
/// </summary>
public interface IUnsavedChangesPrompt
{
    Task<UnsavedChangesDecision> ShowAsync(
        XamlRoot xamlRoot,
        bool isEnglish);
}
