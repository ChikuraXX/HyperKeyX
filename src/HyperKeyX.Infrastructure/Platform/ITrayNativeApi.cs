using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

internal readonly record struct TrayIconRegistration(
    IntPtr WindowHandle,
    uint IconId,
    uint CallbackMessage,
    IntPtr IconHandle,
    string Tooltip);

internal interface ITrayNativeApi
{
    uint RegisterWindowMessage(string messageName);
    IntPtr LoadSmallIcon(string executablePath);
    bool AddIcon(TrayIconRegistration registration);
    bool UpdateIcon(TrayIconRegistration registration);
    bool ShowNotification(IntPtr windowHandle, uint iconId, string title, string message);
    void DeleteIcon(IntPtr windowHandle, uint iconId);
    void DestroyIcon(IntPtr iconHandle);
    uint ShowContextMenu(IntPtr windowHandle, TrayPresentation presentation);
    int GetLastError();
}
