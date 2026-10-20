using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Infrastructure.Interop;
using static HyperKeyX.Infrastructure.Interop.InputNativeMethods;

namespace HyperKeyX.Infrastructure.Input;

internal static class MouseInputPacketFactory
{
    public static InputNativeMethods.INPUT CreateMove(PathPosition position, PathPixelRectangle desktop)
    {
        position = MousePathCoordinateResolver.Quantize(position);
        if (!desktop.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position), "鼠标位置不在虚拟桌面内。");
        var packet = CreatePacket(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE_NOCOALESCE, 0);
        // 绝对输入规范为 0..65535，两端对应桌面首末像素；不受系统相对鼠标加速影响。
        packet.u.mi.dx = Normalize(position.X - desktop.Left, desktop.Width);
        packet.u.mi.dy = Normalize(position.Y - desktop.Top, desktop.Height);
        return packet;
    }

    private static int Normalize(double position, int length)
    {
        if (length == 1) return 0;
        var value = (int)Math.Round(position * 65535 / (length - 1), MidpointRounding.AwayFromZero);
        // 超大桌面的 16 位输入格点可能无法表示指定像素，不能悄悄移动到邻近格点。
        if (Math.Round(value * (length - 1d) / 65535, MidpointRounding.AwayFromZero) != position)
            throw new ArgumentOutOfRangeException(nameof(position), "绝对输入无法精确表示该物理像素。");
        return value;
    }

    public static InputNativeMethods.INPUT CreateButton(
        MouseButtonKind button,
        bool isDown)
    {
        var (flags, data) = (button, isDown) switch
        {
            (MouseButtonKind.Left, true) => (MOUSEEVENTF_LEFTDOWN, 0u),
            (MouseButtonKind.Left, false) => (MOUSEEVENTF_LEFTUP, 0u),
            (MouseButtonKind.Right, true) => (MOUSEEVENTF_RIGHTDOWN, 0u),
            (MouseButtonKind.Right, false) => (MOUSEEVENTF_RIGHTUP, 0u),
            (MouseButtonKind.Middle, true) => (MOUSEEVENTF_MIDDLEDOWN, 0u),
            (MouseButtonKind.Middle, false) => (MOUSEEVENTF_MIDDLEUP, 0u),
            (MouseButtonKind.XButton1, true) => (MOUSEEVENTF_XDOWN, 1u),
            (MouseButtonKind.XButton1, false) => (MOUSEEVENTF_XUP, 1u),
            (MouseButtonKind.XButton2, true) => (MOUSEEVENTF_XDOWN, 2u),
            (MouseButtonKind.XButton2, false) => (MOUSEEVENTF_XUP, 2u),
            _ => throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "鼠标按钮无效。")
        };

        return CreatePacket(flags, data);
    }

    public static InputNativeMethods.INPUT CreateWheel(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wheelDelta),
                "滚轮增量不能为零。");
        }

        return CreatePacket(MOUSEEVENTF_WHEEL, unchecked((uint)wheelDelta));
    }

    private static InputNativeMethods.INPUT CreatePacket(uint flags, uint data)
    {
        return new InputNativeMethods.INPUT
        {
            type = INPUT_MOUSE,
            u = new InputNativeMethods.InputUnion
            {
                mi = new InputNativeMethods.MOUSEINPUT
                {
                    mouseData = data,
                    dwFlags = flags,
                    dwExtraInfo = TurboExtraInfo
                }
            }
        };
    }
}
