using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Application.Abstractions.Platform;

public interface IGlobalShortcutService : IDisposable
{
    event EventHandler<GlobalShortcutAction>? ShortcutInvoked;

    bool IsRegistered { get; }

    OperationResult Register(
        IntPtr windowHandle,
        IReadOnlyCollection<GlobalShortcutDefinition> definitions);

    bool TryHandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam);

    void Unregister();
}
