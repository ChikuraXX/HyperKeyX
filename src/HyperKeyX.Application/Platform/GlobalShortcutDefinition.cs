namespace HyperKeyX.Application.Platform;

public readonly record struct GlobalShortcutDefinition(
    GlobalShortcutAction Action,
    ShortcutModifiers Modifiers,
    int VirtualKey);
