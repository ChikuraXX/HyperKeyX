using System.Collections.ObjectModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Macros.ViewModels;

namespace HyperKeyX.Features.Keyboard.ViewModels;

public sealed class KeyConfigViewModel : TriggerConfigViewModel
{
    private readonly KeyboardKeyIdentity _identity;
    private readonly KeyConfig _config;

    public KeyConfigViewModel(
        int keyId,
        KeyConfig? config = null,
        bool isEnglish = false,
        ObservableCollection<MacroChoiceViewModel>? publishedMacroChoices = null)
        : this(
            KeyboardKeyIdentity.FromStorageKey(keyId, config),
            config,
            isEnglish,
            publishedMacroChoices)
    {
    }

    private KeyConfigViewModel(
        KeyboardKeyIdentity identity,
        KeyConfig? config,
        bool isEnglish,
        ObservableCollection<MacroChoiceViewModel>? publishedMacroChoices)
        : base(InputTriggerIdentity.FromKeyboard(identity), publishedMacroChoices)
    {
        _identity = identity;
        _config = config?.DeepClone() ?? CreateDefaultConfig(identity);
        LoadBehavior(_config, isEnglish);
        UpdateLocalization(isEnglish);
        MarkSaved();
    }

    public int KeyId => _identity.StorageKey;
    public int VirtualKeyCode => _identity.VirtualKeyCode;
    public KeyboardKeyIdentity KeyIdentity => _identity;
    public string KeyDisplayName => TriggerDisplayName;

    public KeyConfig ToConfig()
    {
        _config.TargetVirtualKey = _identity.VirtualKeyCode;
        _config.TargetScanCode = _identity.ScanCode;
        _config.IsExtendedKey = _identity.IsExtendedKey;
        WriteBehaviorTo(_config);
        return _config.DeepClone();
    }

    public override void UpdateLocalization(bool isEnglish)
    {
        SetLanguage(isEnglish);
        TriggerDisplayName = KeyboardKeyDisplayNameFormatter.GetDisplayName(_identity, isEnglish);
        OnPropertyChanged(nameof(KeyDisplayName));
    }

    private static KeyConfig CreateDefaultConfig(KeyboardKeyIdentity identity)
    {
        return new KeyConfig
        {
            TargetVirtualKey = identity.VirtualKeyCode,
            TargetScanCode = identity.ScanCode,
            IsExtendedKey = identity.IsExtendedKey,
            KeyDownDuration = 100,
            KeyUpDuration = 100,
            TriggerMode = 0,
            LongPressThresholdMs = 500
        };
    }

    public static string GetKeyDisplayName(KeyboardKeyIdentity key) =>
        KeyboardKeyDisplayNameFormatter.GetDisplayName(key);

    public static string GetKeyDisplayName(KeyboardKeyIdentity key, bool isEnglish) =>
        KeyboardKeyDisplayNameFormatter.GetDisplayName(key, isEnglish);

    public static string GetKeyDisplayName(int virtualKeyCode) =>
        KeyboardKeyDisplayNameFormatter.GetDisplayName(virtualKeyCode);

    public static string GetKeyDisplayName(int virtualKeyCode, bool isEnglish) =>
        KeyboardKeyDisplayNameFormatter.GetDisplayName(virtualKeyCode, isEnglish);
}
