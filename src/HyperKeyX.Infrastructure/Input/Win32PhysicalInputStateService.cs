using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;

namespace HyperKeyX.Infrastructure.Input;

public sealed class Win32PhysicalInputStateService : IPhysicalInputStateService
{
    private readonly Func<int, short> _read;
    public Win32PhysicalInputStateService() : this(GetAsyncKeyState) { }
    internal Win32PhysicalInputStateService(Func<int, short> read) => _read = read;

    public bool AreAllInputsReleased()
    {
        // 虚拟键 1..6 包含鼠标按钮；只看当前按下高位，不把锁定键切换位当成按住。
        for (var key = 1; key <= 0xFF; key++)
            if ((_read(key) & 0x8000) != 0) return false;
        return true;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
