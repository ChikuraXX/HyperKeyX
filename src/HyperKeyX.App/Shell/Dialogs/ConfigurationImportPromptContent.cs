namespace HyperKeyX.Shell.Dialogs;

public sealed record ConfigurationImportPromptContent(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText);
