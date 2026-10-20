using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 使用 WinUI ContentDialog 显示未保存宏的退出决策。
/// </summary>
public sealed class UnsavedChangesPrompt : IUnsavedChangesPrompt
{
    public async Task<UnsavedChangesDecision> ShowAsync(
        XamlRoot xamlRoot,
        bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var copy = UnsavedChangesPromptContentFactory.Create(isEnglish);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = copy.Title,
            Content = new TextBlock
            {
                Text = copy.Message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = copy.SaveAndExitText,
            SecondaryButtonText = copy.DiscardText,
            CloseButtonText = copy.CancelText,
            DefaultButton = ContentDialogButton.Primary
        };

        return (await dialog.ShowAsync()) switch
        {
            ContentDialogResult.Primary => UnsavedChangesDecision.SaveAndExit,
            ContentDialogResult.Secondary => UnsavedChangesDecision.DiscardAndExit,
            _ => UnsavedChangesDecision.Cancel
        };
    }
}
