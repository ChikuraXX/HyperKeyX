namespace HyperKeyX.Features.Macros.Services;

/// <summary>
/// 宏存在未保存更改时，明确决定导出哪个已验证版本。
/// </summary>
public enum DirtyMacroExportDecision
{
    SaveThenExport = 0,
    ExportSavedBaseline = 1,
    Cancel = 2
}
