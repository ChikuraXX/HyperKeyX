using System.ComponentModel;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Infrastructure.Interop;
using static HyperKeyX.Infrastructure.Interop.InputNativeMethods;

namespace HyperKeyX.Infrastructure.Input;

/// <summary>
/// 通过 SendInput 注入不带坐标的鼠标按钮和垂直滚轮动作。
/// </summary>
public sealed class Win32MouseInputService : IMouseInputService
{
    public void SendButtonDown(MouseButtonKind button) =>
        Send(MouseInputPacketFactory.CreateButton(button, isDown: true));

    public void SendButtonUp(MouseButtonKind button) =>
        Send(MouseInputPacketFactory.CreateButton(button, isDown: false));

    public void SendWheel(int wheelDelta) =>
        Send(MouseInputPacketFactory.CreateWheel(wheelDelta));

    private static void Send(InputNativeMethods.INPUT input)
    {
        var inputs = new[] { input };
        var sent = SendInput(1, inputs, Marshal.SizeOf<InputNativeMethods.INPUT>());
        if (sent != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "SendInput 未能注入鼠标事件。");
        }
    }
}
