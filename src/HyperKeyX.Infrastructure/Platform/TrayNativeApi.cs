using System.Runtime.InteropServices;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Infrastructure.Platform;

internal sealed class TrayNativeApi : ITrayNativeApi
{
    public uint RegisterWindowMessage(string messageName) =>
        TrayNativeMethods.RegisterWindowMessage(messageName);

    public IntPtr LoadSmallIcon(string executablePath)
    {
        var result = TrayNativeMethods.SHGetFileInfo(
            executablePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<TrayNativeMethods.ShellFileInfo>(),
            TrayNativeMethods.SHGFI_ICON | TrayNativeMethods.SHGFI_SMALLICON);
        return result == IntPtr.Zero ? IntPtr.Zero : fileInfo.IconHandle;
    }

    public bool AddIcon(TrayIconRegistration registration)
    {
        var data = CreateData(registration);
        return TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NIM_ADD, ref data);
    }

    public bool UpdateIcon(TrayIconRegistration registration)
    {
        var data = CreateData(registration);
        return TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NIM_MODIFY, ref data);
    }

    public bool ShowNotification(IntPtr windowHandle, uint iconId, string title, string message)
    {
        var data = new TrayNativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(),
            WindowHandle = windowHandle, IconId = iconId,
            Flags = TrayNativeMethods.NIF_INFO,
            InfoFlags = TrayNativeMethods.NIIF_INFO | TrayNativeMethods.NIIF_RESPECT_QUIET_TIME,
            InfoTitle = title, Info = message, Tooltip = string.Empty
        };
        return TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NIM_MODIFY, ref data);
    }

    public void DeleteIcon(IntPtr windowHandle, uint iconId)
    {
        var data = new TrayNativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(),
            WindowHandle = windowHandle,
            IconId = iconId,
            Tooltip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        _ = TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NIM_DELETE, ref data);
    }

    public void DestroyIcon(IntPtr iconHandle)
    {
        if (iconHandle != IntPtr.Zero)
            _ = TrayNativeMethods.DestroyIcon(iconHandle);
    }

    public uint ShowContextMenu(IntPtr windowHandle, TrayPresentation presentation)
    {
        var menu = TrayNativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return 0;

        try
        {
            if (!TrayNativeMethods.AppendMenu(menu, 0, 1001, presentation.OpenText) ||
                !TrayNativeMethods.AppendMenu(menu, 0, 1002, presentation.ToggleText) ||
                !TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MF_SEPARATOR, 0, null) ||
                !TrayNativeMethods.AppendMenu(menu, 0, 1003, presentation.ExitText) ||
                !TrayNativeMethods.GetCursorPos(out var point))
            {
                return 0;
            }

            _ = TrayNativeMethods.SetForegroundWindow(windowHandle);
            var command = TrayNativeMethods.TrackPopupMenuEx(
                menu,
                TrayNativeMethods.TPM_RIGHTBUTTON | TrayNativeMethods.TPM_RETURNCMD,
                point.X,
                point.Y,
                windowHandle,
                IntPtr.Zero);
            _ = TrayNativeMethods.PostMessage(
                windowHandle,
                TrayNativeMethods.WM_NULL,
                IntPtr.Zero,
                IntPtr.Zero);
            return command;
        }
        finally
        {
            _ = TrayNativeMethods.DestroyMenu(menu);
        }
    }

    public int GetLastError() => Marshal.GetLastWin32Error();

    private static TrayNativeMethods.NotifyIconData CreateData(
        TrayIconRegistration registration) =>
        new()
        {
            Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(),
            WindowHandle = registration.WindowHandle,
            IconId = registration.IconId,
            Flags = TrayNativeMethods.NIF_MESSAGE |
                TrayNativeMethods.NIF_ICON |
                TrayNativeMethods.NIF_TIP,
            CallbackMessage = registration.CallbackMessage,
            IconHandle = registration.IconHandle,
            Tooltip = registration.Tooltip,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
}
