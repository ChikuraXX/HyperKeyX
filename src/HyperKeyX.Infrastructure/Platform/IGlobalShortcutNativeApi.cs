namespace HyperKeyX.Infrastructure.Platform;

internal interface IGlobalShortcutNativeApi
{
    bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);
    bool UnregisterHotKey(IntPtr windowHandle, int id);
    int GetLastError();
}
