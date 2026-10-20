using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HyperKeyX.Features.Keyboard.ViewModels;

/// <summary>
/// 单个虚拟键帽的选中、已配置和未保存视觉状态。
/// </summary>
public sealed partial class KeyButtonStateViewModel : ObservableObject
{
    private bool _isEnglish;

    public int KeyId { get; }
    public int VirtualKeyCode { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isTurboHighlighted;

    [ObservableProperty]
    private bool _isMacroHighlighted;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _automationName;

    [ObservableProperty]
    private string _toolTipText;

    public Brush BackgroundBrush
    {
        get
        {
            if (IsSelected && IsTurboHighlighted)
                return new SolidColorBrush(Colors.Goldenrod) { Opacity = 0.28 };
            if (IsSelected && IsMacroHighlighted)
                return new SolidColorBrush(Colors.MediumSeaGreen) { Opacity = 0.28 };
            if (IsSelected)
                return new SolidColorBrush(Colors.DodgerBlue) { Opacity = 0.22 };
            if (IsTurboHighlighted)
                return new SolidColorBrush(Colors.Goldenrod) { Opacity = 0.20 };
            if (IsMacroHighlighted)
                return new SolidColorBrush(Colors.MediumSeaGreen) { Opacity = 0.18 };

            return new SolidColorBrush(Colors.Transparent);
        }
    }

    public Brush BorderBrush
    {
        get
        {
            if (IsSelected)
                return new SolidColorBrush(Colors.DodgerBlue);
            if (IsTurboHighlighted)
                return new SolidColorBrush(Colors.Goldenrod);
            if (IsMacroHighlighted)
                return new SolidColorBrush(Colors.MediumSeaGreen);
            if (IsDirty)
                return new SolidColorBrush(Colors.DeepSkyBlue);

            return new SolidColorBrush(Colors.Gray) { Opacity = 0.35 };
        }
    }

    public Thickness BorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    public KeyButtonStateViewModel(KeyboardKeyIdentity identity, bool isEnglish)
    {
        KeyId = identity.StorageKey;
        VirtualKeyCode = identity.VirtualKeyCode;
        DisplayName = KeyConfigViewModel.GetKeyDisplayName(identity);
        _automationName = string.Empty;
        _toolTipText = string.Empty;
        UpdateLocalization(isEnglish);
    }

    public void RefreshState(KeyConfigViewModel? editor, KeyConfig? savedConfig)
    {
        IsTurboHighlighted = editor?.IsTurboEnabled ?? savedConfig?.IsTurboEnabled ?? false;
        IsMacroHighlighted = editor?.IsMacroEnabled ?? savedConfig?.IsMacroEnabled ?? false;
        IsDirty = editor?.IsDirty ?? false;
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        AutomationName = isEnglish ? $"Key {DisplayName}" : $"按键 {DisplayName}";
        UpdateToolTip();
    }

    partial void OnIsSelectedChanged(bool value) => RaiseVisualPropertiesChanged();
    partial void OnIsTurboHighlightedChanged(bool value) => RaiseVisualPropertiesChanged();
    partial void OnIsMacroHighlightedChanged(bool value) => RaiseVisualPropertiesChanged();

    partial void OnIsDirtyChanged(bool value)
    {
        RaiseVisualPropertiesChanged();
        UpdateToolTip();
    }

    private void UpdateToolTip()
    {
        ToolTipText = IsDirty
            ? (_isEnglish ? $"{DisplayName}, unsaved" : $"{DisplayName}，未保存")
            : DisplayName;
    }

    private void RaiseVisualPropertiesChanged()
    {
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(BorderThickness));
    }
}
