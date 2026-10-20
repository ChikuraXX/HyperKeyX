using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 为键盘矩阵和宏步骤提供一致的按键显示名称。
/// </summary>
public static class KeyboardKeyDisplayNameFormatter
{
    public static string GetDisplayName(KeyboardKeyIdentity key, bool isEnglish = false) =>
        key.StorageKey == KeyboardKeyIdentity.NumpadEnterStorageKey
            ? "Num Enter"
            : GetDisplayName(key.VirtualKeyCode, isEnglish);

    public static string GetDisplayName(int virtualKeyCode, bool isEnglish = false)
    {
        return virtualKeyCode switch
        {
            0x01 => isEnglish ? "Left mouse button" : "左键",
            0x02 => isEnglish ? "Right mouse button" : "右键",
            0x03 => isEnglish ? "Middle mouse button" : "中键",
            0x04 => isEnglish ? "Mouse button 4" : "侧键1",
            0x05 => isEnglish ? "Mouse button 5" : "侧键2",
            0x08 => "Backspace", 0x09 => "Tab",
            0x0C => "Clear", 0x0D => "Enter",
            0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt",
            0x13 => "Pause", 0x14 => "Caps",
            0x15 => "Kana", 0x18 => "IME", 0x19 => "Kanji",
            0x1B => "Esc", 0x1C => "Convert", 0x1D => "NonConvert",
            0x1E => "Accept", 0x1F => "ModeChange", 0x20 => "Space",
            0x21 => "PgUp", 0x22 => "PgDn", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x29 => "Select", 0x2A => "Print", 0x2B => "Execute",
            0x2C => "PrtSc", 0x2D => "Insert", 0x2E => "Delete", 0x2F => "Help",
            >= 0x30 and <= 0x39 => ((char)virtualKeyCode).ToString(),
            >= 0x41 and <= 0x5A => ((char)virtualKeyCode).ToString(),
            0x5B => "Left Win", 0x5C => "Right Win", 0x5D => "Menu", 0x5F => "Sleep",
            0x60 => "Num0", 0x61 => "Num1", 0x62 => "Num2", 0x63 => "Num3",
            0x64 => "Num4", 0x65 => "Num5", 0x66 => "Num6", 0x67 => "Num7",
            0x68 => "Num8", 0x69 => "Num9", 0x6A => "Num*", 0x6B => "Num+",
            0x6C => "Separator", 0x6D => "Num-", 0x6E => "Num.", 0x6F => "Num/",
            >= 0x70 and <= 0x87 => $"F{virtualKeyCode - 0x6F}",
            0x90 => "NumLock", 0x91 => "ScrollLock",
            0xA0 => "Left Shift", 0xA1 => "Right Shift",
            0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl",
            0xA4 => "Left Alt", 0xA5 => "Right Alt",
            0xAD => "Mute", 0xAE => "Volume Down", 0xAF => "Volume Up",
            0xB0 => "Next Track", 0xB1 => "Previous Track", 0xB2 => "Media Stop",
            0xB3 => "Play/Pause", 0xB7 => "Calculator",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-",
            0xBE => ".", 0xBF => "/", 0xC0 => "~",
            0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => $"VK {virtualKeyCode}"
        };
    }
}
