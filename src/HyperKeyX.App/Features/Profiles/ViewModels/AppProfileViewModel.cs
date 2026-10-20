using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Features.Profiles.ViewModels;

/// <summary>
/// 配置列表项，负责把目标进程名称同步回持久化模型。
/// </summary>
public sealed partial class AppProfileViewModel : ObservableObject
{
    public AppProfile Profile { get; }
    public string DisplayName => Profile.DisplayName;
    public bool IsGlobal => string.IsNullOrWhiteSpace(TargetProcessName);

    [ObservableProperty]
    private string _targetProcessName;

    public AppProfileViewModel(AppProfile profile)
    {
        Profile = profile;
        _targetProcessName = profile.TargetProcessName ?? string.Empty;
    }

    partial void OnTargetProcessNameChanged(string value)
    {
        Profile.TargetProcessName = value ?? string.Empty;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsGlobal));
    }
}
