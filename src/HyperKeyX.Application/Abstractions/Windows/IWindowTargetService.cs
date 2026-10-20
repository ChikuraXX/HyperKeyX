using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Abstractions.Windows;

public interface IWindowTargetService
{
    IReadOnlyList<WindowCandidate> EnumerateCandidates(CancellationToken cancellationToken);
    WindowBinding? TryBind(WindowCandidate candidate);
    bool IsCurrent(WindowBinding binding);
    WindowGeometry? GetGeometry(WindowBinding binding);
    bool TryActivate(WindowBinding binding);
    bool IsForeground(WindowBinding binding);
    bool IsHitTarget(WindowBinding binding, PathPosition point);
    IReadOnlyList<PathPixelRectangle> GetMonitors();
    void Release(WindowBinding binding);
}
