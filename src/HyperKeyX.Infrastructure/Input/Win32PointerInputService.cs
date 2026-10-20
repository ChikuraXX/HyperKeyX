using System.ComponentModel;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Infrastructure.Interop;

namespace HyperKeyX.Infrastructure.Input;

public sealed class Win32PointerInputService : IPointerInputService
{
    private readonly Func<PathPosition> _readPosition;
    private readonly Func<IReadOnlyList<PathPixelRectangle>> _readMonitors;
    private readonly Func<InputNativeMethods.INPUT, uint> _send;

    public Win32PointerInputService(IWindowTargetService windows) : this(ReadPhysicalPosition, windows.GetMonitors,
        input => InputNativeMethods.SendInput(1, [input], Marshal.SizeOf<InputNativeMethods.INPUT>())) { }

    internal Win32PointerInputService(Func<PathPosition> readPosition, Func<IReadOnlyList<PathPixelRectangle>> readMonitors,
        Func<InputNativeMethods.INPUT, uint> send)
    {
        _readPosition = readPosition; _readMonitors = readMonitors; _send = send;
    }

    public PathPosition GetPosition()
    {
        var position = _readPosition();
        if (!PathGeometry.IsFinite(position)) throw new InvalidOperationException("无法读取物理鼠标位置。");
        return position;
    }

    public void MoveTo(PathPosition position)
    {
        var packet = CreatePacket(position, _readMonitors());
        if (_send(packet) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput 未能注入鼠标移动。");
    }

    public void ValidatePosition(PathPosition position, IReadOnlyList<PathPixelRectangle> monitors) =>
        _ = CreatePacket(position, monitors);

    private static InputNativeMethods.INPUT CreatePacket(PathPosition position, IReadOnlyList<PathPixelRectangle> monitors)
    {
        var context = new PathCoordinateContext(monitors, default);
        if (!context.ContainsPosition(position))
            throw new ArgumentOutOfRangeException(nameof(position), "鼠标位置不可达。");
        var left = monitors.Min(r => r.Left);
        var top = monitors.Min(r => r.Top);
        var width = monitors.Max(r => r.Right) - left + 1;
        var height = monitors.Max(r => r.Bottom) - top + 1;
        if (width > int.MaxValue || height > int.MaxValue)
            throw new InvalidOperationException("虚拟桌面范围超出绝对输入编码范围。");
        return MouseInputPacketFactory.CreateMove(position, new(left, top, (int)width, (int)height));
    }

    private static PathPosition ReadPhysicalPosition() => MouseNativeMethods.GetPhysicalCursorPos(out var point)
        ? new(point.x, point.y) : throw new Win32Exception();
}
