namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 在用户确认前展示的全配置导入摘要，不携带可变领域对象。
/// </summary>
public sealed record ConfigurationImportSummary(
    int ProfileCount,
    int MacroCount,
    int KeyboardReferenceCount,
    int MouseReferenceCount,
    bool WasMigrated);
