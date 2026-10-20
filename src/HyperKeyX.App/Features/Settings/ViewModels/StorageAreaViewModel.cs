using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Application.Storage;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed class StorageAreaViewModel : ObservableObject
{
    public StorageAreaViewModel(StorageAreaUsage usage, bool isEnglish)
    {
        Id = usage.Id;
        Path = usage.Path;
        Bytes = Math.Max(0, usage.Bytes);
        Exists = usage.Exists;
        Warning = usage.Warning;
        Glyph = ResolveGlyph(usage.Id);
        UpdateLocalization(isEnglish);
    }

    public StorageAreaId Id { get; }
    public string Path { get; }
    public long Bytes { get; }
    public bool Exists { get; }
    public string? Warning { get; }
    public string Glyph { get; }
    public bool CanOpen => !string.IsNullOrWhiteSpace(Path);
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SizeText { get; private set; } = string.Empty;
    public string OpenButtonText { get; private set; } = string.Empty;

    public void UpdateLocalization(bool isEnglish)
    {
        (Title, Description) = ResolveText(Id, isEnglish);
        SizeText = StorageSizeFormatter.Format(Bytes);
        OpenButtonText = isEnglish ? "Open folder" : "打开目录";
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(OpenButtonText));
    }

    private static (string Title, string Description) ResolveText(
        StorageAreaId id,
        bool isEnglish) => id switch
    {
        StorageAreaId.Assets => isEnglish
            ? ("Custom assets", "Local backgrounds and appearance resources")
            : ("自定义资源", "本地背景图片和界面资源"),
        StorageAreaId.Cache => isEnglish
            ? ("Cache", "Temporary application files")
            : ("缓存", "应用临时文件"),
        StorageAreaId.Logs => isEnglish
            ? ("Logs", "Local diagnostic logs")
            : ("日志", "本地诊断日志"),
        _ => isEnglish
            ? ("Application data", "Profiles, preferences, and local backups")
            : ("应用数据", "配置文件、偏好和本地备份")
    };

    private static string ResolveGlyph(StorageAreaId id) => id switch
    {
        StorageAreaId.Assets => "\uEB9F",
        StorageAreaId.Cache => "\uE74D",
        StorageAreaId.Logs => "\uE8A5",
        _ => "\uE8B7"
    };
}
