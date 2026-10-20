namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 描述一次编辑所替换的连续步骤片段以及编辑前后的选中项。
/// </summary>
internal sealed record MacroEditTransaction(
    int StartIndex,
    IReadOnlyList<MacroStepSnapshot> Before,
    IReadOnlyList<MacroStepSnapshot> After,
    Guid? SelectionBefore,
    Guid? SelectionAfter,
    MacroPathContextSnapshot? ContextBefore = null,
    MacroPathContextSnapshot? ContextAfter = null);
