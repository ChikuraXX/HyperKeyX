using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Infrastructure.Windows;

/// <summary>原生查询、生命周期监视和激活的可注入边界，测试不触达真实窗口。</summary>
public interface IWindowNativeApi : IDisposable
{
    IReadOnlyList<nint> EnumerateWindows();
    WindowCandidate? ReadCandidate(nint handle);
    WindowGeometry? ReadGeometry(nint handle);
    IDisposable? WatchLifetime(nint handle, Action invalidated);
    bool FlushLifetimeEvents();
    bool Activate(nint handle);
    nint GetForegroundWindow();
    nint WindowAt(PathPosition point);
    bool IsChild(nint parent, nint child);
    IReadOnlyList<PathPixelRectangle> GetMonitors();
}
