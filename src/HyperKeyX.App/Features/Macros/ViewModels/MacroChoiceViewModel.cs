namespace HyperKeyX.Features.Macros.ViewModels;

public sealed record MacroChoiceViewModel(
    Guid MacroId,
    string Name,
    MacroDefinitionViewModel Source)
{
    public int StepCount => Source.GetSavedModel()?.Steps.Count ?? 0;
}
