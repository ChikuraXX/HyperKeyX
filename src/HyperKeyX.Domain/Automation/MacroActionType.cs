namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 定义宏序列中可持久化的键盘与鼠标动作。
/// </summary>
public enum MacroActionType
{
    KeyDown = 0,
    KeyUp = 1,
    Delay = 2,
    MouseButtonDown = 3,
    MouseButtonUp = 4,
    MouseWheel = 5,
    MousePath = 6
}
