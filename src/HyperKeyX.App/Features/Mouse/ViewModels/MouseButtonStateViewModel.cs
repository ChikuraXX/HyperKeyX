using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HyperKeyX.Features.Mouse.ViewModels;

/// <summary>
/// 表示鼠标按钮选择器中的选中、已配置和未保存状态。
/// </summary>
public sealed partial class MouseButtonStateViewModel : ObservableObject
{
    private bool _isEnglish;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isTurboHighlighted;

    [ObservableProperty]
    private bool _isMacroHighlighted;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _automationName = string.Empty;

    [ObservableProperty]
    private string _toolTipText = string.Empty;

    public MouseButtonStateViewModel(MouseButtonKind button, bool isEnglish)
    {
        if (!InputTriggerIdentity.FromMouse(button).IsValid)
            throw new ArgumentOutOfRangeException(nameof(button), button, "不支持该鼠标按钮。");

        Button = button;
        UpdateLocalization(isEnglish);
    }

    public MouseButtonKind Button { get; }

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

    public void RefreshState(
        MouseButtonConfigViewModel? editor,
        MouseButtonConfig? savedConfig)
    {
        IsTurboHighlighted = editor?.IsTurboEnabled ?? savedConfig?.IsTurboEnabled ?? false;
        IsMacroHighlighted = editor?.IsMacroEnabled ?? savedConfig?.IsMacroEnabled ?? false;
        IsDirty = editor?.IsDirty ?? false;
    }

    public void UpdateLocalization(bool isEnglish)
    {
        _isEnglish = isEnglish;
        DisplayName = Button switch
        {
            MouseButtonKind.Left => isEnglish ? "Left button" : "左键",
            MouseButtonKind.Right => isEnglish ? "Right button" : "右键",
            MouseButtonKind.Middle => isEnglish ? "Middle button" : "中键",
            MouseButtonKind.XButton1 => isEnglish ? "Button 4" : "侧键 1",
            MouseButtonKind.XButton2 => isEnglish ? "Button 5" : "侧键 2",
            _ => string.Empty
        };
        AutomationName = isEnglish ? $"Mouse {DisplayName}" : $"鼠标{DisplayName}";
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
