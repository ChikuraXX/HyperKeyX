using System.Runtime.InteropServices;

namespace HyperKeyX.Infrastructure.Platform;

internal sealed class GlobalShortcutNativeApi : IGlobalShortcutNativeApi
{
    public bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey) =>
        GlobalShortcutNativeMethods.RegisterHotKey(
            windowHandle,
            id,
            modifiers,
            virtualKey);

    public bool UnregisterHotKey(IntPtr windowHandle, int id) =>
        GlobalShortcutNativeMethods.UnregisterHotKey(windowHandle, id);

    public int GetLastError() => Marshal.GetLastWin32Error();
}
