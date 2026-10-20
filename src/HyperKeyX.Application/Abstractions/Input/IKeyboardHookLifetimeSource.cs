namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>可选的只读键盘钩子生命周期来源，用任务身份区分不同安装。</summary>
public interface IKeyboardHookLifetimeSource
{
    /// <summary>
    /// 当前安装的终止任务；每次新安装返回独立任务，成功完成表示该安装已结束且不会再交付输入。
    /// 尚未安装时返回已完成任务。任务不因订阅方取消而改变，不暴露安装或停止操作。
    /// </summary>
    Task CurrentLifetime { get; }
}
