using HyperKeyX.Features.Automation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 使用 WinUI ContentDialog 展示完整配置导入摘要。
/// </summary>
public sealed class ConfigurationImportPrompt : IConfigurationImportPrompt
{
    public async Task<bool> ShowAsync(
        ConfigurationImportSummary summary,
        bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var xamlRoot = App.MainWindow.Content?.XamlRoot ??
            throw new InvalidOperationException("主窗口尚未准备好显示导入确认。");
        var copy = ConfigurationImportPromptContentFactory.Create(summary, isEnglish);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = copy.Title,
            Content = new TextBlock
            {
                Text = copy.Message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            },
            PrimaryButtonText = copy.ConfirmText,
            CloseButtonText = copy.CancelText,
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
