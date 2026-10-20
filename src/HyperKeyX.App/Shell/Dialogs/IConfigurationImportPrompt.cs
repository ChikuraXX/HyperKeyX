using HyperKeyX.Features.Automation.Services;

namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 在完整配置进入替换草稿前显示解析摘要并取得显式确认。
/// </summary>
public interface IConfigurationImportPrompt
{
    Task<bool> ShowAsync(
        ConfigurationImportSummary summary,
        bool isEnglish);
}
