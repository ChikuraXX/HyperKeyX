namespace HyperKeyX.Domain.Automation.Paths;

public sealed class MousePathGroup
{
    public Guid GroupId { get; set; }
    public MousePathCoordinateMode CoordinateMode { get; set; }
    public Guid? TargetId { get; set; }
    public PathPosition Origin { get; set; }
    public MousePathRecordedFrame RecordedFrame { get; set; } = new();
    public MousePathRandomizationSettings Randomization { get; set; } = new();

    public MousePathGroup DeepClone() => new()
    {
        GroupId = GroupId,
        CoordinateMode = CoordinateMode,
        TargetId = TargetId,
        Origin = Origin,
        RecordedFrame = RecordedFrame with { },
        Randomization = Randomization with { }
    };
}
