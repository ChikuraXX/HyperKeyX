using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Abstractions.Storage;
using HyperKeyX.Application.Abstractions.Updates;
using HyperKeyX.Domain.Preferences;
using Microsoft.UI.Xaml;

namespace HyperKeyX.Features.Settings.ViewModels;

/// <summary>
/// 管理可独立保存的全局设置状态和本地化显示内容。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly UiScaleMode[] UiScaleModes =
    [
        UiScaleMode.System,
        UiScaleMode.Percent100,
        UiScaleMode.Percent125,
        UiScaleMode.Percent150,
        UiScaleMode.Percent175,
        UiScaleMode.Percent200
    ];

    private readonly IAppPreferencesStore _store;
    private AppPreferences _preferences;
    private bool _isRestoringSelection;
    private bool _isUpdatingLocalizedOptions;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedStartupPageIndex;

    [ObservableProperty]
    private int _selectedUiScaleIndex;

    [ObservableProperty]
    private SettingsCategoryId _selectedCategory;

    [ObservableProperty]
    private bool _isErrorOpen;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<string> LanguageOptions { get; } = new();
    public ObservableCollection<string> ThemeOptions { get; } = new();
    public ObservableCollection<string> StartupPageOptions { get; } = new();
    public ObservableCollection<string> UiScaleOptions { get; } = new();
    public ObservableCollection<SettingsCategoryViewModel> Categories { get; } = new();
    public IReadOnlyList<FutureFeatureViewModel> FutureFeatures { get; }
    public FutureFeatureViewModel NotificationsFeature { get; }
    public UiScaleMetrics UiScaleMetrics { get; }
    public AppPreferences CurrentPreferences => _preferences;
    public bool IsGeneralCategoryActive => SelectedCategory == SettingsCategoryId.General;
    public bool IsAppearanceCategoryActive => SelectedCategory == SettingsCategoryId.Appearance;
    /// <summary>当前分类是否为软件介绍与更新。</summary>
    public bool IsAboutCategoryActive => SelectedCategory == SettingsCategoryId.About;
    /// <summary>关于分类使用独立标题，其他分类继续显示设置标题。</summary>
    public string ActiveSettingsTitleText => IsAboutCategoryActive ? Localize("关于", "About") : SettingsTitleText;
    public bool IsEnglish => AppLanguageResolver.ResolveEnglish(SelectedLanguageIndex);
    public ElementTheme AppTheme => SelectedThemeIndex switch
    {
        (int)AppThemeMode.Light => ElementTheme.Light,
        (int)AppThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public string SettingsTitleText => Localize("设置", "Settings");
    public string SettingsDescriptionText => Localize("全局应用偏好", "Global application preferences");
    public string DefaultStartupPageText => Localize("默认打开页面", "Default startup page");
    public string DefaultStartupPageDescriptionText =>
        Localize("选择应用启动后显示的页面", "Choose the page shown when the app starts");
    public string LanguageText => Localize("语言", "Language");
    public string LanguageDescriptionText =>
        Localize("设置应用的显示语言", "Choose the application display language");
    public string ThemeText => Localize("主题", "Theme");
    public string ThemeDescriptionText =>
        Localize("设置应用的明暗主题", "Choose the application color theme");
    public string UiScaleText => Localize("界面缩放", "Interface scale");
    public string UiScaleDescriptionText =>
        Localize("调整应用内容大小，Windows 缩放仍然生效", "Adjust app content size while keeping Windows scaling active");

    public SettingsViewModel(IAppPreferencesStore store)
        : this(
            store,
            EmptyStorageInspector.Instance,
            UnavailablePathLauncher.Instance,
            UnavailableStartupRegistrationService.Instance,
            UnavailableBackgroundAssetService.Instance)
    {
    }

    /// <summary>组合根使用完整平台能力构建设置，包括官方更新查询与外部链接。</summary>
    public SettingsViewModel(
        IAppPreferencesStore store, IStorageInspector storageInspector, IPathLauncher pathLauncher,
        IStartupRegistrationService startupRegistration, IBackgroundAssetService backgroundAssets,
        IInstalledFontCatalog fontCatalog, IReleaseUpdateService updates, IExternalLinkLauncher links,
        IUpdatePackageDownloader downloader, bool enableUpdates = false)
        : this(store, storageInspector, pathLauncher, startupRegistration, backgroundAssets, fontCatalog)
    {
        _releaseUpdates = updates;
        _externalLinks = links;
        _updateDownloader = downloader;
        _updatesEnabled = enableUpdates;
    }

    /// <summary>由组合根注入本机字体目录，旧构造方式继续用于不涉及字体枚举的调用。</summary>
    public SettingsViewModel(
        IAppPreferencesStore store,
        IStorageInspector storageInspector,
        IPathLauncher pathLauncher,
        IStartupRegistrationService startupRegistration,
        IBackgroundAssetService backgroundAssets,
        IInstalledFontCatalog fontCatalog)
        : this(store, storageInspector, pathLauncher, startupRegistration, backgroundAssets)
    {
        _fontCatalog = fontCatalog;
    }

    public SettingsViewModel(
        IAppPreferencesStore store,
        IBackgroundAssetService backgroundAssets)
        : this(
            store,
            EmptyStorageInspector.Instance,
            UnavailablePathLauncher.Instance,
            UnavailableStartupRegistrationService.Instance,
            backgroundAssets)
    {
    }

    public SettingsViewModel(
        IAppPreferencesStore store,
        IStartupRegistrationService startupRegistration)
        : this(
            store,
            EmptyStorageInspector.Instance,
            UnavailablePathLauncher.Instance,
            startupRegistration,
            UnavailableBackgroundAssetService.Instance)
    {
    }

    public SettingsViewModel(
        IAppPreferencesStore store,
        IStorageInspector storageInspector,
        IPathLauncher pathLauncher)
        : this(
            store,
            storageInspector,
            pathLauncher,
            UnavailableStartupRegistrationService.Instance,
            UnavailableBackgroundAssetService.Instance)
    {
    }

    public SettingsViewModel(
        IAppPreferencesStore store,
        IStorageInspector storageInspector,
        IPathLauncher pathLauncher,
        IStartupRegistrationService startupRegistration)
        : this(
            store,
            storageInspector,
            pathLauncher,
            startupRegistration,
            UnavailableBackgroundAssetService.Instance)
    {
    }

    public SettingsViewModel(
        IAppPreferencesStore store,
        IStorageInspector storageInspector,
        IPathLauncher pathLauncher,
        IStartupRegistrationService startupRegistration,
        IBackgroundAssetService backgroundAssets)
    {
        _store = store;
        InitializeStorage(storageInspector, pathLauncher);
        _preferences = AppPreferencesNormalizer.Normalize(store.Load());
        ActiveShortcuts = _preferences.Shortcuts;
        InitializeShortcutDraft();
        InitializeLifecycle(startupRegistration);
        InitializeNotificationPreferences();
        InitializeAppearance(backgroundAssets);
        _selectedLanguageIndex = (int)_preferences.Language;
        _selectedThemeIndex = (int)_preferences.Theme;
        _selectedStartupPageIndex = (int)_preferences.StartupPage;
        _selectedUiScaleIndex = Array.IndexOf(UiScaleModes, _preferences.UiScale);
        if (_selectedUiScaleIndex < 0)
            _selectedUiScaleIndex = 0;
        UiScaleMetrics = new UiScaleMetrics(_preferences.UiScale);
        _selectedCategory = _preferences.LastSettingsCategory;
        Categories.Add(new(SettingsCategoryId.General, "\uE713"));
        Categories.Add(new(SettingsCategoryId.Appearance, "\uE790"));
        Categories.Add(new(SettingsCategoryId.About, "\uE946"));
        NotificationsFeature = new(FutureFeatureId.Notifications);
        FutureFeatures =
        [
            NotificationsFeature
        ];
        RefreshLocalizedOptions();
    }

    public AppPageId ResolveInitialPage()
    {
        return _preferences.StartupPage switch
        {
            StartupPageMode.Profiles => AppPageId.Profiles,
            StartupPageMode.Mouse => AppPageId.Mouse,
            StartupPageMode.Macros => AppPageId.Macros,
            StartupPageMode.LastVisited => _preferences.LastVisitedPage == AppPageId.Settings
                ? AppPageId.Keyboard : _preferences.LastVisitedPage,
            _ => AppPageId.Keyboard
        };
    }

    public void RecordVisitedPage(AppPageId page)
    {
        if (_preferences.LastVisitedPage == page)
            return;

        _ = TrySave(_preferences with { LastVisitedPage = page });
    }

    public void ShowWarning(string message)
    {
        ShowError(message);
    }

    partial void OnSelectedLanguageIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringSelection || _isUpdatingLocalizedOptions)
            return;

        var normalized = AppLanguageResolver.NormalizeIndex(newValue);
        if (normalized != newValue ||
            !TrySave(_preferences with { Language = (AppLanguageMode)normalized }))
        {
            RestoreSelection(() => SelectedLanguageIndex = oldValue);
            return;
        }

        RefreshLocalizedOptions();
        RaiseLocalizedPropertiesChanged();
    }

    partial void OnSelectedThemeIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringSelection || _isUpdatingLocalizedOptions)
            return;

        var normalized = Enum.IsDefined((AppThemeMode)newValue)
            ? newValue
            : (int)AppThemeMode.System;
        if (normalized != newValue)
        {
            RestoreSelection(() => SelectedThemeIndex = oldValue);
            return;
        }

        if (!TrySave(_preferences with { Theme = (AppThemeMode)normalized }))
        {
            RestoreSelection(() => SelectedThemeIndex = oldValue);
            return;
        }

        OnPropertyChanged(nameof(AppTheme));
    }

    partial void OnSelectedStartupPageIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringSelection || _isUpdatingLocalizedOptions)
            return;

        var normalized = Enum.IsDefined((StartupPageMode)newValue)
            ? newValue
            : (int)StartupPageMode.Keyboard;
        if (normalized != newValue ||
            !TrySave(_preferences with { StartupPage = (StartupPageMode)normalized }))
        {
            RestoreSelection(() => SelectedStartupPageIndex = oldValue);
        }
    }

    partial void OnSelectedUiScaleIndexChanged(int oldValue, int newValue)
    {
        if (_isRestoringSelection || _isUpdatingLocalizedOptions)
            return;

        if (newValue < 0 || newValue >= UiScaleModes.Length)
        {
            RestoreSelection(() => SelectedUiScaleIndex = oldValue);
            return;
        }

        var mode = UiScaleModes[newValue];
        if (!TrySave(_preferences with { UiScale = mode }))
        {
            RestoreSelection(() => SelectedUiScaleIndex = oldValue);
            return;
        }

        UiScaleMetrics.Apply(mode);
    }

    partial void OnSelectedCategoryChanged(SettingsCategoryId oldValue, SettingsCategoryId newValue)
    {
        if (_isRestoringSelection)
            return;

        if (!Enum.IsDefined(newValue) ||
            !TrySave(_preferences with { LastSettingsCategory = newValue }))
        {
            RestoreSelection(() => SelectedCategory = oldValue);
            return;
        }

        OnPropertyChanged(nameof(IsGeneralCategoryActive));
        OnPropertyChanged(nameof(IsAppearanceCategoryActive));
        OnPropertyChanged(nameof(IsAboutCategoryActive));
        OnPropertyChanged(nameof(ActiveSettingsTitleText));
    }

    private bool TrySave(AppPreferences candidate)
    {
        candidate = AppPreferencesNormalizer.Normalize(candidate);
        if (_store.Save(candidate))
        {
            _preferences = candidate;
            OnPropertyChanged(nameof(IsAccentContrastWarningOpen));
            IsErrorOpen = false;
            OnPropertyChanged(nameof(CurrentPreferences));
            return true;
        }

        ShowError(LocalizeCurrentPreferences("无法保存设置。", "Could not save settings."));
        return false;
    }

    private string Localize(string chinese, string english) =>
        IsEnglish ? english : chinese;

    private string LocalizeCurrentPreferences(string chinese, string english) =>
        AppLanguageResolver.ResolveEnglish((int)_preferences.Language) ? english : chinese;

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private void RestoreSelection(Action restore)
    {
        _isRestoringSelection = true;
        try
        {
            restore();
        }
        finally
        {
            _isRestoringSelection = false;
        }
    }

    private void RefreshLocalizedOptions()
    {
        var isEnglish = IsEnglish;
        var preservedLanguageIndex = SelectedLanguageIndex;
        var preservedThemeIndex = SelectedThemeIndex;
        var preservedStartupPageIndex = SelectedStartupPageIndex;
        var preservedUiScaleIndex = SelectedUiScaleIndex;
        var preservedFontIndex = SelectedFontIndex;
        var preservedAccentModeIndex = SelectedAccentModeIndex;
        var preservedBackdropIndex = SelectedBackdropIndex;
        _isUpdatingLocalizedOptions = true;
        try
        {
            ReplaceOptions(LanguageOptions, isEnglish
                ? new[] { "Simplified Chinese", "English", "Use system setting" }
                : new[] { "简体中文", "English", "跟随系统" });
            ReplaceOptions(ThemeOptions, isEnglish
                ? new[] { "Light", "Dark", "Use system setting" }
                : new[] { "浅色", "深色", "跟随系统" });
            ReplaceOptions(StartupPageOptions, isEnglish
                ? new[] { "Keyboard", "Profiles", "Last visited page", "Mouse", "Macros" }
                : new[] { "键盘", "配置文件", "上次打开页面", "鼠标", "宏库" });
            ReplaceOptions(UiScaleOptions, isEnglish
                ? new[] { "Use system setting", "100%", "125%", "150%", "175%", "200%" }
                : new[] { "跟随系统", "100%", "125%", "150%", "175%", "200%" });
            ReplaceOptions(FontOptions, isEnglish
                ? new[] { "Use system setting", "Segoe UI Variable", "Microsoft YaHei UI" }
                : new[] { "跟随系统", "Segoe UI Variable", "Microsoft YaHei UI" });
            ReplaceOptions(AccentModeOptions, isEnglish
                ? new[] { "Use system setting", "Custom" }
                : new[] { "跟随系统", "自定义" });
            ReplaceOptions(BackdropOptions, isEnglish
                ? new[] { "Use system setting", "Mica", "Acrylic", "Solid" }
                : new[] { "跟随系统", "Mica", "Acrylic", "纯色" });

            foreach (var category in Categories)
                category.UpdateLocalization(isEnglish);
            foreach (var feature in FutureFeatures)
                feature.UpdateLocalization(isEnglish);
            RefreshStorageLocalization();

            SelectedLanguageIndex = preservedLanguageIndex;
            SelectedThemeIndex = preservedThemeIndex;
            SelectedStartupPageIndex = preservedStartupPageIndex;
            SelectedUiScaleIndex = preservedUiScaleIndex;
            SelectedFontIndex = preservedFontIndex;
            SelectedAccentModeIndex = preservedAccentModeIndex;
            SelectedBackdropIndex = preservedBackdropIndex;
        }
        finally
        {
            _isUpdatingLocalizedOptions = false;
        }
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        while (target.Count < values.Count)
            target.Add(values[target.Count]);

        for (var index = 0; index < values.Count; index++)
        {
            if (!string.Equals(target[index], values[index], StringComparison.Ordinal))
                target[index] = values[index];
        }

        while (target.Count > values.Count)
            target.RemoveAt(target.Count - 1);
    }

    private void RaiseLocalizedPropertiesChanged()
    {
        OnPropertyChanged(nameof(ActiveSettingsTitleText));
        RaiseUpdateLocalizationChanged();
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(SettingsDescriptionText));
        OnPropertyChanged(nameof(DefaultStartupPageText));
        OnPropertyChanged(nameof(DefaultStartupPageDescriptionText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(LanguageDescriptionText));
        OnPropertyChanged(nameof(ThemeText));
        OnPropertyChanged(nameof(ThemeDescriptionText));
        OnPropertyChanged(nameof(UiScaleText));
        OnPropertyChanged(nameof(UiScaleDescriptionText));
        RaiseAppearanceLocalizationChanged();
        RaiseShortcutLocalizationChanged();
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StorageDescriptionText));
        OnPropertyChanged(nameof(RefreshStorageText));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(StorageLoadingText));
        OnPropertyChanged(nameof(StartWithWindowsText));
        OnPropertyChanged(nameof(StartWithWindowsDescriptionText));
        OnPropertyChanged(nameof(TrayIconText));
        OnPropertyChanged(nameof(TrayIconDescriptionText));
        OnPropertyChanged(nameof(StartMinimizedToTrayText));
        OnPropertyChanged(nameof(StartMinimizedToTrayDescriptionText));
        RaiseNotificationLocalizationChanged();
        OnPropertyChanged(nameof(ToggleOnText));
        OnPropertyChanged(nameof(ToggleOffText));
    }
}
