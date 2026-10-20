namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>
/// 通过低级键盘钩子拦截全局键盘输入的服务接口
/// </summary>
public interface IGlobalHookService
{
    event EventHandler<KeyboardInputEventArgs>? AnyKeyStateChanged;
    void Start();
    void Stop();
    bool IsRunning { get; }
}
