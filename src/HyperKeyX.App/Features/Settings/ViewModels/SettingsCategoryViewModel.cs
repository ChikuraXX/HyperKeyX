using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Features.Settings.ViewModels;

/// <summary>
/// 表示具有稳定标识和图标的设置分类。
/// </summary>
public sealed partial class SettingsCategoryViewModel : ObservableObject
{
    public SettingsCategoryViewModel(SettingsCategoryId id, string glyph)
    {
        Id = id;
        Glyph = glyph;
    }

    public SettingsCategoryId Id { get; }
    public string Glyph { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    public void UpdateLocalization(bool isEnglish)
    {
        Title = Id switch
        {
            SettingsCategoryId.About => isEnglish ? "About" : "关于",
            SettingsCategoryId.Appearance => isEnglish ? "Appearance" : "界面设置",
            _ => isEnglish ? "General" : "通用设置"
        };
    }
}
