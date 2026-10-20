using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 表示可由多个键盘或鼠标触发器共享的宏定义。
/// </summary>
public sealed class MacroDefinition
{
    public Guid MacroId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<MacroStep> Steps { get; set; } = [];
    public List<MousePathGroup> PathGroups { get; set; } = [];
    public List<MacroWindowTarget> WindowTargets { get; set; } = [];

    public MacroDefinition DeepClone() => new()
    {
        MacroId = MacroId,
        Name = Name ?? string.Empty,
        Description = Description ?? string.Empty,
        PathGroups = PathGroups.Select(group => group.DeepClone()).ToList(),
        WindowTargets = WindowTargets.Select(target => target.DeepClone()).ToList(),
        Steps = (Steps ?? [])
            .Where(step => step is not null)
            .Select(step => step.DeepClone())
            .ToList()
    };
}
