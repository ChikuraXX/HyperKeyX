using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Application.Appearance;
using HyperKeyX.Domain.Appearance;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    private IInstalledFontCatalog? _fontCatalog;

    /// <summary>在后台枚举字体，保持界面线程可响应；失败由选择器统一反馈。</summary>
    public Task<IReadOnlyList<InstalledFontFamily>> LoadInstalledFontsAsync()
    {
        var catalog = _fontCatalog ?? throw new InvalidOperationException("未提供本机字体目录。");
        return Task.Run(catalog.ReadInstalledFamilies);
    }
    private IBackgroundAssetService _backgroundAssets =
        UnavailableBackgroundAssetService.Instance;
    private bool _isRestoringAppearance;

    [ObservableProperty]
    private int _selectedFontIndex;

    [ObservableProperty]
    private int _selectedAccentModeIndex;

    [ObservableProperty]
    private string _customAccentColor = AppPreferences.DefaultCustomAccentColor;

    [ObservableProperty]
    private int _selectedBackdropIndex;

    [ObservableProperty]
    private bool _customBackgroundEnabled;

    private string? _customBackgroundAssetName;
    private double _backgroundOverlayOpacity =
        AppearanceOptionResolver.DefaultOverlayOpacity;

    public ObservableCollection<string> FontOptions { get; } = [];

    /// <summary>当前字体的家族名称；未指定时返回旧版预设的名称。</summary>
    public string SelectedFontFamilyName => _preferences.FontFamily ??
        (FontOptions.Count > (int)_preferences.Font ? FontOptions[(int)_preferences.Font] : "Segoe UI Variable Text");

    /// <summary>保存字体选择；失败时保留原字体，空值表示恢复系统预设。</summary>
    public bool SelectInstalledFont(string? familyName)
    {
        if (!TrySave(_preferences with { FontFamily = familyName, Font = AppFontOption.System }))
            return false;
        RestoreAppearance(() => SelectedFontIndex = (int)AppFontOption.System);
        OnPropertyChanged(nameof(SelectedFontFamilyName));
        return true;
    }
    public ObservableCollection<string> AccentModeOptions { get; } = [];
    public ObservableCollection<string> BackdropOptions { get; } = [];

    public string? CustomBackgroundAssetName
    {
        get => _customBackgroundAssetName;
        private set => SetProperty(ref _customBackgroundAssetName, value);
    }

    public double BackgroundOverlayOpacity
    {
        get => _backgroundOverlayOpacity;
        set
        {
            if (SetProperty(
                    ref _backgroundOverlayOpacity,
                    AppearanceOptionResolver.NormalizeOverlayOpacity(value)))
            {
                OnPropertyChanged(nameof(BackgroundOverlayOpacityPercentText));
            }
        }
    }

    public bool IsCustomAccentActive => SelectedAccentModeIndex == (int)AccentMode.Custom;
    public bool HasCustomBackground => !string.IsNullOrWhiteSpace(CustomBackgroundAssetName);
    public bool CanEnableCustomBackground =>
        HasCustomBackground && _backgroundAssets.Exists(CustomBackgroundAssetName);
    public bool IsBackgroundOverlayOpacityVisible => CustomBackgroundEnabled;

    public string FontText => Localize("字体", "Font");
    public string FontDescriptionText => Localize(
        "设置应用界面字体，不影响键盘键帽和图标",
        "Choose the app font without changing keycaps or icons");
    public string AccentText => Localize("强调色", "Accent color");
    public string AccentDescriptionText => Localize(
        "跟随 Windows 或使用应用专属颜色",
        "Follow Windows or use an app-only color");
    public string BackdropText => Localize("窗口材质", "Window material");
    public string BackdropDescriptionText => Localize(
        "选择 Mica、Acrylic 或纯色背景",
        "Choose Mica, Acrylic, or a solid background");
    public string CustomBackgroundText => Localize("自定义背景", "Custom background");
    public string CustomBackgroundDescriptionText => Localize(
        "使用复制到应用目录的本地图片",
        "Use a local image copied into the app folder");
    public string ChooseBackgroundImageText => Localize("选择图片", "Choose image");
    public string ChooseAccentColorText => Localize("选择强调色", "Choose accent color");
    public string BackgroundOverlayOpacityText => Localize(
        "背景可读性",
        "Background readability");
    public string BackgroundOverlayOpacityDescriptionText => Localize(
        "调整内容遮罩强度，数值越高文字越清晰",
        "Adjust the content overlay; higher values improve text clarity");
    public string BackgroundOverlayOpacityPercentText =>
        $"{Math.Round(BackgroundOverlayOpacity * 100):0}%";

    partial void OnSelectedFontIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringAppearance || _isUpdatingLocalizedOptions)
            return;

        if (!Enum.IsDefined((AppFontOption)newValue) ||
            !TrySave(_preferences with { Font = (AppFontOption)newValue, FontFamily = null }))
        {
            RestoreAppearance(() => SelectedFontIndex = oldValue);
        }
        OnPropertyChanged(nameof(SelectedFontFamilyName));
    }

    partial void OnSelectedAccentModeIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringAppearance || _isUpdatingLocalizedOptions)
            return;

        if (!Enum.IsDefined((AccentMode)newValue))
        {
            RestoreAppearance(() => SelectedAccentModeIndex = oldValue);
            return;
        }

        if (!TrySave(_preferences with { AccentMode = (AccentMode)newValue }))
        {
            RestoreAppearance(() => SelectedAccentModeIndex = oldValue);
            return;
        }

        OnPropertyChanged(nameof(IsCustomAccentActive));
    }

    partial void OnCustomAccentColorChanged(string? oldValue, string newValue)
    {
        if (_isRestoringAppearance)
            return;

        if (!AccentColorValidator.TryNormalize(
                newValue,
                out var normalized))
        {
            ShowError(Localize("强调色格式无效。", "Invalid accent color format."));
            RestoreAppearance(() => CustomAccentColor =
                oldValue ?? AppPreferences.DefaultCustomAccentColor);
            return;
        }

        if (string.Equals(normalized, oldValue, StringComparison.Ordinal))
        {
            RestoreAppearance(() => CustomAccentColor = normalized);
            return;
        }

        if (!TrySave(_preferences with { CustomAccentColor = normalized }))
        {
            RestoreAppearance(() => CustomAccentColor =
                oldValue ?? AppPreferences.DefaultCustomAccentColor);
            return;
        }

        if (!string.Equals(normalized, newValue, StringComparison.Ordinal))
            RestoreAppearance(() => CustomAccentColor = normalized);
    }

    partial void OnSelectedBackdropIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringAppearance || _isUpdatingLocalizedOptions)
            return;

        if (!Enum.IsDefined((BackdropMode)newValue) ||
            !TrySave(_preferences with { Backdrop = (BackdropMode)newValue }))
        {
            RestoreAppearance(() => SelectedBackdropIndex = oldValue);
        }
    }

    partial void OnCustomBackgroundEnabledChanged(bool oldValue, bool newValue)
    {
        if (_isRestoringAppearance)
            return;

        if (newValue && !CanEnableCustomBackground)
        {
            ShowError(Localize(
                "请先选择有效的本地背景图片。",
                "Choose a valid local background image first."));
            RestoreAppearance(() => CustomBackgroundEnabled = oldValue);
            return;
        }

        if (!TrySave(_preferences with { CustomBackgroundEnabled = newValue }))
        {
            RestoreAppearance(() => CustomBackgroundEnabled = oldValue);
            return;
        }

        OnPropertyChanged(nameof(IsBackgroundOverlayOpacityVisible));
    }

    public async Task ImportBackgroundAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return;

        var result = await _backgroundAssets.ImportAsync(sourcePath, cancellationToken);
        if (_isDisposed || cancellationToken.IsCancellationRequested)
            return;

        if (!result.Succeeded ||
            result.AssetName is null ||
            !_backgroundAssets.Exists(result.AssetName))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SettingsViewModel] 背景导入失败: {result.ErrorMessage}");
            ShowError(Localize(
                "无法导入背景图片。",
                "Could not import the background image."));
            return;
        }

        var candidate = _preferences with
        {
            CustomBackgroundAssetName = result.AssetName,
            CustomBackgroundEnabled = true
        };
        if (!TrySave(candidate))
            return;

        RestoreAppearance(() =>
        {
            CustomBackgroundAssetName = result.AssetName;
            CustomBackgroundEnabled = true;
        });
        RaiseBackgroundStateChanged();
    }

    public void CommitBackgroundOverlayOpacity()
    {
        var normalized = AppearanceOptionResolver.NormalizeOverlayOpacity(
            BackgroundOverlayOpacity);
        if (normalized.Equals(_preferences.BackgroundOverlayOpacity))
        {
            BackgroundOverlayOpacity = normalized;
            return;
        }

        var previous = _preferences.BackgroundOverlayOpacity;
        if (TrySave(_preferences with { BackgroundOverlayOpacity = normalized }))
        {
            BackgroundOverlayOpacity = normalized;
            return;
        }

        BackgroundOverlayOpacity = previous;
    }

    private void InitializeAppearance(IBackgroundAssetService backgroundAssets)
    {
        _backgroundAssets = backgroundAssets ??
            throw new ArgumentNullException(nameof(backgroundAssets));
        if (_preferences.CustomBackgroundEnabled &&
            !_backgroundAssets.Exists(_preferences.CustomBackgroundAssetName))
        {
            // 丢失的图片仅在内存中回退，构造阶段不主动写配置文件。
            _preferences = _preferences with { CustomBackgroundEnabled = false };
        }

        RestoreAppearance(() =>
        {
            SelectedFontIndex = (int)_preferences.Font;
            SelectedAccentModeIndex = (int)_preferences.AccentMode;
            CustomAccentColor = _preferences.CustomAccentColor;
            SelectedBackdropIndex = (int)_preferences.Backdrop;
            CustomBackgroundAssetName = _preferences.CustomBackgroundAssetName;
            CustomBackgroundEnabled = _preferences.CustomBackgroundEnabled;
            BackgroundOverlayOpacity = _preferences.BackgroundOverlayOpacity;
        });
    }

    /// <summary>已保存的自定义颜色对比度偏低时仅显示提示，不阻止应用。</summary>
    public bool IsAccentContrastWarningOpen => _preferences.AccentMode == AccentMode.Custom &&
        !AccentColorValidator.TryNormalize(_preferences.CustomAccentColor, _preferences.Theme, out _);

    public string AccentContrastWarningText => Localize(
        "强调色与背景的对比度不足，已应用所选颜色。",
        "The accent color has low contrast against the background. Your color is applied.");

    private void RestoreAppearance(Action restore)
    {
        _isRestoringAppearance = true;
        try
        {
            restore();
        }
        finally
        {
            _isRestoringAppearance = false;
        }
    }

    private void RaiseBackgroundStateChanged()
    {
        OnPropertyChanged(nameof(HasCustomBackground));
        OnPropertyChanged(nameof(CanEnableCustomBackground));
        OnPropertyChanged(nameof(IsBackgroundOverlayOpacityVisible));
    }

    private void RaiseAppearanceLocalizationChanged()
    {
        OnPropertyChanged(nameof(SelectedFontFamilyName));
        OnPropertyChanged(nameof(FontText));
        OnPropertyChanged(nameof(FontDescriptionText));
        OnPropertyChanged(nameof(AccentContrastWarningText));
        OnPropertyChanged(nameof(AccentText));
        OnPropertyChanged(nameof(AccentDescriptionText));
        OnPropertyChanged(nameof(BackdropText));
        OnPropertyChanged(nameof(BackdropDescriptionText));
        OnPropertyChanged(nameof(CustomBackgroundText));
        OnPropertyChanged(nameof(CustomBackgroundDescriptionText));
        OnPropertyChanged(nameof(ChooseBackgroundImageText));
        OnPropertyChanged(nameof(ChooseAccentColorText));
        OnPropertyChanged(nameof(BackgroundOverlayOpacityText));
        OnPropertyChanged(nameof(BackgroundOverlayOpacityDescriptionText));
    }

    private sealed class UnavailableBackgroundAssetService : IBackgroundAssetService
    {
        public static UnavailableBackgroundAssetService Instance { get; } = new();

        public Task<BackgroundAssetImportResult> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackgroundAssetImportResult.Failure(
                "背景图片服务不可用。"));
        }

        public string? ResolvePath(string? assetName) => null;

        public bool Exists(string? assetName) => false;
    }
}
