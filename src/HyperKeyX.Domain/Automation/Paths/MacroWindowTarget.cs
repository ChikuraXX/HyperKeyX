namespace HyperKeyX.Domain.Automation.Paths;

public sealed record MacroWindowTarget
{
    public Guid TargetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? WindowClass { get; set; }
    public string? Title { get; set; }
    public WindowTitleMatchMode TitleMatchMode { get; set; }
    public bool RequireDistinctWindow { get; set; }

    public MacroWindowTarget DeepClone() => this with { };
}
