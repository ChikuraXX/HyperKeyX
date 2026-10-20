using HyperKeyX.Features.Automation.Services;

namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 生成完整配置导入摘要的本地化确认文案。
/// </summary>
public static class ConfigurationImportPromptContentFactory
{
    public static ConfigurationImportPromptContent Create(
        ConfigurationImportSummary summary,
        bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (isEnglish)
        {
            var migration = summary.WasMigrated
                ? " The file was migrated from a supported legacy format and must be reviewed."
                : string.Empty;
            return new ConfigurationImportPromptContent(
                "Confirm full configuration import",
                $"The file contains {summary.ProfileCount} profiles, " +
                $"{summary.MacroCount} macros, " +
                $"{summary.KeyboardReferenceCount} keyboard references, and " +
                $"{summary.MouseReferenceCount} mouse references. " +
                "Confirmation stages a replacement only; disk and runtime remain unchanged " +
                $"until Save All succeeds.{migration}",
                "Stage replacement",
                "Cancel");
        }

        var chineseMigration = summary.WasMigrated
            ? " 文件已从受支持的旧格式迁移，请在保存前检查内容。"
            : string.Empty;
        return new ConfigurationImportPromptContent(
            "确认导入完整配置",
            $"文件包含 {summary.ProfileCount} 个配置、" +
            $"{summary.MacroCount} 个宏、" +
            $"{summary.KeyboardReferenceCount} 个键盘引用和 " +
            $"{summary.MouseReferenceCount} 个鼠标引用。" +
            "确认后只会暂存替换；磁盘和运行时将在“保存全部”成功前保持不变。" +
            chineseMigration,
            "暂存替换",
            "取消");
    }
}
