namespace HyperKeyX.Shell.Dispatching;

/// <summary>
/// 隔离 WinUI 线程派发，使工作区逻辑可在无窗口测试环境中运行。
/// </summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    bool TryEnqueue(Action action);
}
