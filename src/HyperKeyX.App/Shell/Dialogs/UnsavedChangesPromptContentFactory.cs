namespace HyperKeyX.Shell.Dialogs;

/// <summary>
/// 创建覆盖完整编辑工作区的退出提示中英文文案。
/// </summary>
public static class UnsavedChangesPromptContentFactory
{
    public static UnsavedChangesPromptContent Create(bool isEnglish) => isEnglish
        ? new UnsavedChangesPromptContent(
            "Unsaved changes",
            "Save all changes to macros, triggers, or imported configuration " +
            "before exiting? Invalid content must be corrected before it can be saved.",
            "Save all and exit",
            "Discard changes",
            "Cancel")
        : new UnsavedChangesPromptContent(
            "存在未保存的更改",
            "退出前是否保存宏、触发器或导入配置的全部更改？存在错误的内容必须先修正才能保存。",
            "全部保存并退出",
            "放弃更改",
            "取消");
}
