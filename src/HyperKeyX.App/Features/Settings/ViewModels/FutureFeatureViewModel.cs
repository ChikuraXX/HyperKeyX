using CommunityToolkit.Mvvm.ComponentModel;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed class FutureFeatureViewModel : ObservableObject
{
    public FutureFeatureViewModel(FutureFeatureId id)
    {
        Id = id;
        Glyph = id switch
        {
            FutureFeatureId.UpdateChannel => "\uE8D7",
            FutureFeatureId.Notifications => "\uE7F4",
            _ => "\uE895"
        };
    }

    public FutureFeatureId Id { get; }
    public string Glyph { get; }
    public bool IsAvailable => false;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string AvailabilityText { get; private set; } = string.Empty;
    public IReadOnlyList<string> AvailabilityOptions { get; private set; } = [];

    public void UpdateLocalization(bool isEnglish)
    {
        (Title, Description) = Id switch
        {
            FutureFeatureId.UpdateChannel => isEnglish
                ? ("Update channel", "Channel selection will be available with online updates")
                : ("更新通道", "在线更新开放后可选择下载通道"),
            FutureFeatureId.Notifications => isEnglish
                ? ("Online messages", "Online update and announcement messages are reserved for a future version")
                : ("在线消息", "在线更新与公告消息将在后续版本开放"),
            _ => isEnglish
                ? ("Automatic updates", "Online version checks are reserved for a future version")
                : ("自动检查更新", "在线版本检查功能将在后续版本开放")
        };
        AvailabilityText = isEnglish ? "Coming later" : "暂未开放";
        AvailabilityOptions = [AvailabilityText];
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(AvailabilityOptions));
    }
}
