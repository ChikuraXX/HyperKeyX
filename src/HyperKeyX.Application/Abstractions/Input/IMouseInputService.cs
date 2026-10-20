using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Abstractions.Input;

public interface IMouseInputService
{
    void SendButtonDown(MouseButtonKind button);
    void SendButtonUp(MouseButtonKind button);
    void SendWheel(int wheelDelta);
}
