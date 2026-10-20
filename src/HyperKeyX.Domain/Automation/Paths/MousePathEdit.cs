namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>以稳定点身份保存覆盖值；新增点用 InsertAfterPointId 记录有序位置。</summary>
public sealed record MousePathEdit
{
    public Guid PointId { get; set; }
    public Guid? InsertAfterPointId { get; set; }
    public bool IsDeleted { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public long? OffsetUs { get; set; }
}
