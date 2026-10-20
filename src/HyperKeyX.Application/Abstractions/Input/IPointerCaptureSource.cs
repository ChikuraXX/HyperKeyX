namespace HyperKeyX.Application.Abstractions.Input;

public interface IPointerCaptureSource
{
    IDisposable SubscribeMovement(Action<MouseInputEvent> callback);
}
