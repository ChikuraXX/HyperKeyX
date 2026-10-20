using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.Services;

public enum MacroDraftChangeKind
{
    Upsert = 0,
    Delete = 1
}

/// <summary>
/// 表示一次保存范围内的宏新增、更新或删除快照。
/// </summary>
public sealed class MacroDraftChange
{
    private readonly MacroDefinition? _macro;

    private MacroDraftChange(
        Guid macroId,
        MacroDraftChangeKind kind,
        MacroDefinition? macro)
    {
        MacroId = macroId;
        Kind = kind;
        _macro = macro?.DeepClone();
    }

    public Guid MacroId { get; }
    public MacroDraftChangeKind Kind { get; }

    public static MacroDraftChange Upsert(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        return new MacroDraftChange(
            macro.MacroId,
            MacroDraftChangeKind.Upsert,
            macro);
    }

    public static MacroDraftChange Delete(Guid macroId) =>
        new(macroId, MacroDraftChangeKind.Delete, macro: null);

    internal MacroDefinition? CreateMacroSnapshot() => _macro?.DeepClone();
}
