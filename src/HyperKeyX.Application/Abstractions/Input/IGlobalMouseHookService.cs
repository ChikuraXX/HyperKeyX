namespace HyperKeyX.Application.Abstractions.Input;

public interface IGlobalMouseHookService
{
    event EventHandler<MouseInputEventArgs>? AnyMouseInput;
    void Start();
    void Stop();
    bool IsRunning { get; }
}
