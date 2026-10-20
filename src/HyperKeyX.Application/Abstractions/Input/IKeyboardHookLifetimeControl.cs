namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>可选的按安装身份停止能力，避免检查后重装导致旧所有者停止新钩子。</summary>
public interface IKeyboardHookLifetimeControl : IKeyboardHookLifetimeSource
{
    /// <summary>
    /// 仅向仍匹配 lifetime 的安装发出停止请求；匹配检查与请求必须原子进行。
    /// 返回 false 表示该安装已不存在或已被替换；true 表示请求已发出，终止以生命周期任务为准。
    /// </summary>
    bool StopIfCurrent(Task lifetime);
}
