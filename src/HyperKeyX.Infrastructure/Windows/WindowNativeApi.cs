using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Infrastructure.Interop;
using static HyperKeyX.Infrastructure.Interop.WindowNativeMethods;

namespace HyperKeyX.Infrastructure.Windows;

public sealed class WindowNativeApi : IWindowNativeApi
{
    private readonly WindowLifetimeHub _lifetime = new();

    public IReadOnlyList<nint> EnumerateWindows()
    {
        var handles = new List<nint>();
        EnumWindowProc callback = (handle, _) => { handles.Add(handle); return true; };
        if (!WindowNativeMethods.EnumWindows(callback, 0)) throw new Win32Exception();
        GC.KeepAlive(callback);
        return handles.AsReadOnly();
    }

    public WindowCandidate? ReadCandidate(nint handle)
    {
        if (!IsWindow(handle)) return null;
        _ = ProcessNativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId > int.MaxValue) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            var started = process.StartTime.ToUniversalTime().Ticks;
            string? path;
            try { path = process.MainModule?.FileName; }
            catch (Win32Exception) { path = null; }
            var windowClass = new StringBuilder(256);
            if (GetClassNameW(handle, windowClass, windowClass.Capacity) == 0) return null;
            var title = new StringBuilder(Math.Min(65_536, GetWindowTextLengthW(handle) + 1));
            _ = GetWindowTextW(handle, title, title.Capacity);
            _ = ProcessNativeMethods.GetWindowThreadProcessId(handle, out var checkedProcessId);
            return checkedProcessId == processId && IsWindow(handle)
                ? new(handle, (int)processId, started, name, path, windowClass.ToString(), title.ToString(), IsWindowVisible(handle)) : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
        { return null; }
    }

    public WindowGeometry? ReadGeometry(nint handle)
    {
        using var dpi = new PhysicalDpiScope();
        if (!GetClientRect(handle, out var rectangle)) return null;
        var first = new Point { X = rectangle.Left, Y = rectangle.Top };
        var last = new Point { X = rectangle.Right, Y = rectangle.Bottom };
        if (!ClientToScreen(handle, ref first) || !ClientToScreen(handle, ref last)) return null;
        return MapGeometry(new(first.X, first.Y), new(last.X, last.Y), GetDpiForWindow(handle),
            GetWindowDpiAwarenessContext(handle), IsWindowVisible(handle), IsIconic(handle), GetMonitors());
    }

    public static WindowGeometry? MapGeometry(PathPosition first, PathPosition last, uint dpi,
        nint awarenessContext, bool visible, bool minimized, IReadOnlyList<PathPixelRectangle> monitors)
    {
        if (!PathGeometry.IsFinite(first) || !PathGeometry.IsFinite(last) ||
            first.X is < int.MinValue or > int.MaxValue || first.Y is < int.MinValue or > int.MaxValue ||
            last.X is < int.MinValue or > int.MaxValue || last.Y is < int.MinValue or > int.MaxValue ||
            first != MousePathCoordinateResolver.Quantize(first) || last != MousePathCoordinateResolver.Quantize(last))
            return null;
        var width = last.X - first.X;
        var height = last.Y - first.Y;
        if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue ||
            monitors.Count == 0 || monitors.Any(r => !r.IsValid)) return null;
        var layout = string.Join("|", monitors.OrderBy(r => r.Left).ThenBy(r => r.Top).ThenBy(r => r.Width).ThenBy(r => r.Height)
            .Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Left},{r.Top},{r.Width},{r.Height}")));
        return new(new((int)first.X, (int)first.Y, (int)width, (int)height), dpi,
            awarenessContext, visible, minimized, layout);
    }

    public IDisposable? WatchLifetime(nint handle, Action invalidated) => _lifetime.Watch(handle, invalidated);
    public bool FlushLifetimeEvents() => _lifetime.Flush();
    public bool Activate(nint handle) => SetForegroundWindow(handle);
    public nint GetForegroundWindow() => ProcessNativeMethods.GetForegroundWindow();
    public bool IsChild(nint parent, nint child) => WindowNativeMethods.IsChild(parent, child);

    public nint WindowAt(PathPosition point)
    {
        point = MousePathCoordinateResolver.Quantize(point);
        if (!PathGeometry.IsFinite(point) || point.X is < int.MinValue or > int.MaxValue || point.Y is < int.MinValue or > int.MaxValue)
            return 0;
        using var dpi = new PhysicalDpiScope();
        return WindowFromPoint(new Point { X = (int)point.X, Y = (int)point.Y });
    }

    public IReadOnlyList<PathPixelRectangle> GetMonitors()
    {
        using var dpi = new PhysicalDpiScope();
        var monitors = new List<PathPixelRectangle>();
        EnumMonitorProc callback = (nint _, nint _, ref Rectangle rectangle, nint _) =>
        {
            var width = (long)rectangle.Right - rectangle.Left;
            var height = (long)rectangle.Bottom - rectangle.Top;
            if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue) return false;
            monitors.Add(new(rectangle.Left, rectangle.Top, (int)width, (int)height));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new Win32Exception();
        GC.KeepAlive(callback);
        return monitors.AsReadOnly();
    }

    public void Dispose() => _lifetime.Dispose();

    private sealed class PhysicalDpiScope : IDisposable
    {
        private readonly nint _previous;
        public PhysicalDpiScope()
        {
            // PMv2 作用域内读取桌面物理像素，离开时恢复调用线程原有 DPI 上下文。
            _previous = SetThreadDpiAwarenessContext(-4);
            if (_previous == 0) throw new Win32Exception();
        }
        public void Dispose()
        {
            if (SetThreadDpiAwarenessContext(_previous) == 0) throw new Win32Exception();
        }
    }
}
