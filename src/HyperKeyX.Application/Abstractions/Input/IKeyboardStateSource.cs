using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>提供钩子当前跟踪的按下快照，包含安装前的系统状态和被拦截的物理事件。</summary>
public interface IKeyboardStateSource
{
    /// <summary>返回独立快照；不产生输入，也不把当前按住的键作为新的触发动作。</summary>
    IReadOnlyCollection<KeyboardKeyIdentity> GetPressedKeys();
}
