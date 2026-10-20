namespace HyperKeyX.Domain.Automation.Paths;

public sealed class MousePathDefinition
{
    public Guid GroupId { get; set; }
    public MousePathMode Mode { get; set; } = MousePathMode.Continuous;
    public MousePathSourceKind SourceKind { get; set; } = MousePathSourceKind.Recorded;
    public MousePathSource Source { get; set; } = new([]);
    public List<MousePathEdit> Edits { get; set; } = [];
    public MousePathProcessingSettings Processing { get; set; } = new();
    public MousePathTimingSettings Timing { get; set; } = new();

    public MousePathDefinition DeepClone() => new()
    {
        GroupId = GroupId,
        Mode = Mode,
        SourceKind = SourceKind,
        // 源块已冻结，可安全共享；编辑及设置仍必须逐份隔离。
        Source = Source,
        Edits = Edits.Select(edit => edit with { }).ToList(),
        Processing = Processing with { },
        Timing = Timing with { }
    };
}
