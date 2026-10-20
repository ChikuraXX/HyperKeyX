using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Abstractions.Input;

public interface IPointerInputService
{
    PathPosition GetPosition();
    void MoveTo(PathPosition position);
    void ValidatePosition(PathPosition position, IReadOnlyList<PathPixelRectangle> monitors)
    {
        if (!new PathCoordinateContext(monitors, default).ContainsPosition(position))
            throw new ArgumentOutOfRangeException(nameof(position));
    }
}
