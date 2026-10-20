namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 表示一个可持久化、可注入的物理键身份。
/// StorageKey 用于配置字典；VirtualKeyCode、ScanCode 和 IsExtendedKey 用于 Win32 输入。
/// </summary>
public readonly record struct KeyboardKeyIdentity(
    int StorageKey,
    int VirtualKeyCode,
    int ScanCode,
    bool IsExtendedKey)
{
    // 主回车和小键盘回车都使用 VK_RETURN，只能通过扩展位区分。
    public const int NumpadEnterStorageKey = 0x1000D;

    public bool IsValid =>
        VirtualKeyCode is > 0 and <= 0xFF &&
        (StorageKey == NumpadEnterStorageKey || StorageKey is > 0 and <= 0xFF);

    public static KeyboardKeyIdentity FromStorageKey(int storageKey, KeyConfig? config = null)
    {
        if (storageKey == NumpadEnterStorageKey ||
            (storageKey == 0x0D && config?.IsExtendedKey == true))
            return new KeyboardKeyIdentity(NumpadEnterStorageKey, 0x0D, 0x1C, true);

        // 普通配置以字典键为准，修复导入数据中 TargetVirtualKey 与键 ID 不一致的问题。
        var virtualKeyCode = storageKey is > 0 and <= 0xFF
            ? storageKey
            : config?.TargetVirtualKey ?? 0;
        var scanCode = Math.Max(config?.TargetScanCode ?? 0, 0);
        var isExtended = config?.IsExtendedKey == true || IsKnownExtendedKey(virtualKeyCode);

        return new KeyboardKeyIdentity(storageKey, virtualKeyCode, scanCode, isExtended);
    }

    public static KeyboardKeyIdentity FromHook(int virtualKeyCode, int scanCode, int flags)
    {
        var isExtended = (flags & 0x01) != 0;
        var storageKey = virtualKeyCode == 0x0D && isExtended
            ? NumpadEnterStorageKey
            : virtualKeyCode;

        return new KeyboardKeyIdentity(storageKey, virtualKeyCode, scanCode, isExtended);
    }

    public static KeyboardKeyIdentity FromMacroStep(MacroStep step)
    {
        var storageKey = step.VirtualKeyCode == 0x0D && step.IsExtendedKey
            ? NumpadEnterStorageKey
            : step.VirtualKeyCode;

        return new KeyboardKeyIdentity(
            storageKey,
            step.VirtualKeyCode,
            Math.Max(step.ScanCode, 0),
            step.IsExtendedKey || IsKnownExtendedKey(step.VirtualKeyCode));
    }

    private static bool IsKnownExtendedKey(int virtualKeyCode)
    {
        return virtualKeyCode is
            0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or
            0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0xA3 or 0xA5 or
            0xAD or 0xAE or 0xAF or 0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xB7;
    }
}

/// <summary>
/// 全局钩子向上层发布的完整键盘事件。
/// </summary>
public readonly record struct KeyboardInputEvent(
    KeyboardKeyIdentity Key,
    bool IsDown,
    bool IsRepeat,
    long TimestampUs = 0,
    long Sequence = 0,
    bool IsInjected = false);
