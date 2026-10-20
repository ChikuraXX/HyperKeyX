using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 保存宏步骤及其稳定行身份，供事务历史安全回放。
/// </summary>
internal sealed record MacroStepSnapshot
{
    public MacroStepSnapshot(Guid stepId, MacroStep model)
    {
        if (stepId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(stepId));

        ArgumentNullException.ThrowIfNull(model);
        StepId = stepId;
        Model = CopyModel(model);
    }

    public Guid StepId { get; }
    public MacroStep Model { get; }

    public static MacroStepSnapshot FromRow(MacroStepEditorViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new MacroStepSnapshot(row.StepId, row.ToModel());
    }

    public MacroStepEditorViewModel CreateRow(bool isEnglish) =>
        new(StepId, CopyModel(Model), isEnglish);

    public static MacroStep CopyModel(MacroStep value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.DeepClone();
    }
}
