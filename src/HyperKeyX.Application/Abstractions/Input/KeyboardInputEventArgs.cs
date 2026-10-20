using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>
/// 携带物理键盘事件以及必须在钩子返回前确定的阻止决策。
/// </summary>
public sealed class KeyboardInputEventArgs : EventArgs
{
    public KeyboardInputEvent Input { get; }
    public bool SuppressOriginalInput { get; set; }

    public KeyboardInputEventArgs(KeyboardInputEvent input)
    {
        Input = input;
    }
}
