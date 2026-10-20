namespace HyperKeyX.Domain.Automation;

public enum InputTriggerKind
{
    Keyboard = 0,
    MouseButton = 1
}

/// <summary>
/// 在不同输入设备之间保持唯一语义的触发器身份。
/// </summary>
public readonly record struct InputTriggerIdentity(InputTriggerKind Kind, int Code)
{
    public bool IsValid => Kind switch
    {
        InputTriggerKind.Keyboard =>
            Code == KeyboardKeyIdentity.NumpadEnterStorageKey || Code is > 0 and <= 0xFF,
        InputTriggerKind.MouseButton =>
            Code is >= (int)MouseButtonKind.Left and <= (int)MouseButtonKind.XButton2,
        _ => false
    };

    public static InputTriggerIdentity FromKeyboard(KeyboardKeyIdentity key) =>
        new(InputTriggerKind.Keyboard, key.StorageKey);

    public static InputTriggerIdentity FromMouse(MouseButtonKind button) =>
        new(InputTriggerKind.MouseButton, (int)button);
}
