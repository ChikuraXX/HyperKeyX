using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Shell.Navigation;

public enum MainSection
{
    Keyboard,
    Mouse,
    Macros,
    Profiles,
    Settings
}

/// <summary>
/// 保存主导航的唯一页面状态，避免多个可见性布尔值互相冲突。
/// </summary>
public sealed partial class MainNavigationState : ObservableObject
{
    [ObservableProperty]
    private MainSection _activeSection = MainSection.Keyboard;

    public bool IsKeyboardActive => ActiveSection == MainSection.Keyboard;
    public bool IsMouseActive => ActiveSection == MainSection.Mouse;
    public bool IsMacrosActive => ActiveSection == MainSection.Macros;
    public bool IsProfilesActive => ActiveSection == MainSection.Profiles;
    public bool IsSettingsActive => ActiveSection == MainSection.Settings;

    public static MainSection FromPageId(AppPageId page) => page switch
    {
        AppPageId.Mouse => MainSection.Mouse,
        AppPageId.Macros => MainSection.Macros,
        AppPageId.Profiles => MainSection.Profiles,
        AppPageId.Settings => MainSection.Settings,
        _ => MainSection.Keyboard
    };

    public static AppPageId ToPageId(MainSection section) => section switch
    {
        MainSection.Mouse => AppPageId.Mouse,
        MainSection.Macros => AppPageId.Macros,
        MainSection.Profiles => AppPageId.Profiles,
        MainSection.Settings => AppPageId.Settings,
        _ => AppPageId.Keyboard
    };

    public bool TryNavigate(string? tag)
    {
        var section = tag?.ToLowerInvariant() switch
        {
            "keyboard" => MainSection.Keyboard,
            "mouse" => MainSection.Mouse,
            "macros" => MainSection.Macros,
            "profiles" => MainSection.Profiles,
            "settings" => MainSection.Settings,
            _ => (MainSection?)null
        };

        if (section == null)
            return false;

        ActiveSection = section.Value;
        return true;
    }

    partial void OnActiveSectionChanged(MainSection value)
    {
        OnPropertyChanged(nameof(IsKeyboardActive));
        OnPropertyChanged(nameof(IsMouseActive));
        OnPropertyChanged(nameof(IsMacrosActive));
        OnPropertyChanged(nameof(IsProfilesActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }
}
