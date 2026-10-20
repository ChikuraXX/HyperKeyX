using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface ITrayIconService : IDisposable
{
    event EventHandler<TrayCommand>? CommandInvoked;

    uint CallbackMessage { get; }
    bool IsVisible { get; }

    OperationResult Show(IntPtr windowHandle, TrayPresentation presentation);

    OperationResult Update(TrayPresentation presentation);

    bool TryHandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam);

    void Hide();
}
