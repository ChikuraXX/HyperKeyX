using System.Collections.ObjectModel;

namespace HyperKeyX.Domain.Automation.Paths;

/// <summary>封口后的源点只读；调用者不能通过传入数组或返回集合改写历史。</summary>
public sealed class MousePathSource
{
    private readonly ReadOnlyCollection<MousePathPoint> _points;

    public MousePathSource(IEnumerable<MousePathPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = Array.AsReadOnly(points.ToArray());
    }

    public IReadOnlyList<MousePathPoint> Points => _points;
}
