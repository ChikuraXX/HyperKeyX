using System.ComponentModel;
using System.Runtime.InteropServices;
using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation;
using static HyperKeyX.Infrastructure.Interop.InputNativeMethods;

namespace HyperKeyX.Infrastructure.Input;

/// <summary>
/// 通过 SendInput 注入带有物理身份信息的键盘事件。
/// </summary>
public sealed class Win32InputService : IWin32InputService, IDisposable
{
    private bool _disposed;
    private bool _periodSet;

    public Win32InputService()
    {
        // 只有系统确认成功后，Dispose 才对应调用 timeEndPeriod。
        _periodSet = timeBeginPeriod(1) == 0;
    }

    public void SendKeyDown(KeyboardKeyIdentity key)
    {
        SendKey(key, isKeyUp: false);
    }

    public void SendKeyUp(KeyboardKeyIdentity key)
    {
        SendKey(key, isKeyUp: true);
    }

    private void SendKey(KeyboardKeyIdentity key, bool isKeyUp)
    {
        ThrowIfDisposed();
        if (!key.IsValid)
            throw new ArgumentOutOfRangeException(nameof(key), "按键身份无效。");

        var flags = isKeyUp ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN;
        if (key.IsExtendedKey)
            flags |= KEYEVENTF_EXTENDEDKEY;

        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)key.VirtualKeyCode,
                        wScan = (ushort)Math.Clamp(key.ScanCode, 0, ushort.MaxValue),
                        dwFlags = flags,
                        dwExtraInfo = TurboExtraInfo
                    }
                }
            }
        };

        var sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput 未能注入键盘事件。");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_periodSet)
        {
            _ = timeEndPeriod(1);
            _periodSet = false;
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Win32InputService));
    }
}
