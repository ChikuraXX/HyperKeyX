using System.Globalization;
using HyperKeyX.Application.Abstractions.Appearance;
using HyperKeyX.Domain.Appearance;
using HyperKeyX.Domain.Preferences;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace HyperKeyX.Shell.Appearance;

public sealed class WindowAppearanceService : IWindowAppearanceService, IDisposable
{
    private readonly IBackgroundAssetService _backgroundAssets;
    private readonly UISettings _uiSettings = new();
    private bool _isDisposed;

    public WindowAppearanceService(IBackgroundAssetService backgroundAssets)
    {
        _backgroundAssets = backgroundAssets;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public event EventHandler? SystemAppearanceChanged;

    public AppearanceApplyResult Apply(
        Window window,
        WindowAppearanceHost host,
        AppPreferences preferences)
    {
        var normalized = AppPreferencesNormalizer.Normalize(preferences);
        try
        {
            host.ApplicationRoot.RequestedTheme = ResolveTheme(normalized.Theme);
            host.ApplicationRoot.Resources["ContentControlThemeFontFamily"] =
                new FontFamily(AppearanceOptionResolver.FontFamilyName(normalized));
            ApplyBackdrop(window, normalized.Backdrop);
            ApplyAccent(host.ApplicationRoot, ResolveAccent(normalized));
            return ApplyBackground(host, normalized);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WindowAppearanceService] 应用窗口外观失败: {exception}");
            window.SystemBackdrop = null;
            ApplySolidFallback(host, normalized.Theme);
            return AppearanceApplyResult.Warning(Localize(
                normalized,
                "当前环境不支持所选外观，已回退到纯色背景。",
                "The selected appearance is unavailable; a solid background is being used."));
        }
    }

    private static void ApplyBackdrop(Window window, BackdropMode mode)
    {
        switch (AppearanceOptionResolver.NormalizeBackdrop(mode))
        {
            case BackdropMode.Acrylic:
                if (window.SystemBackdrop is not DesktopAcrylicBackdrop)
                    window.SystemBackdrop = new DesktopAcrylicBackdrop();
                break;
            case BackdropMode.Solid:
                window.SystemBackdrop = null;
                break;
            default:
                if (window.SystemBackdrop is not MicaBackdrop)
                    window.SystemBackdrop = new MicaBackdrop();
                break;
        }
    }

    private Color ResolveAccent(AppPreferences preferences)
    {
        if (preferences.AccentMode == AccentMode.Custom &&
            TryParseColor(preferences.CustomAccentColor, out var customAccent))
        {
            return customAccent;
        }

        return _uiSettings.GetColorValue(UIColorType.Accent);
    }

    private static void ApplyAccent(FrameworkElement root, Color accent)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources["HyperKeyXAccentBrush"] is not
            SolidColorBrush accentBrush)
        {
            throw new InvalidOperationException("缺少应用强调色资源。");
        }

        Microsoft.UI.Xaml.Application.Current.Resources["HyperKeyXAccentColor"] = accent;
        accentBrush.Color = accent;
        UpdateAccentBrush(root, "AccentFillColorDefaultBrush", accent);
        UpdateAccentBrush(root, "AccentFillColorSecondaryBrush", ColorHelper.FromArgb(230, accent.R, accent.G, accent.B));
        UpdateAccentBrush(root, "AccentFillColorTertiaryBrush", ColorHelper.FromArgb(204, accent.R, accent.G, accent.B));
        UpdateAccentBrush(root, "AccentTextFillColorPrimaryBrush", accent);
        UpdateAccentBrush(root, "AccentTextFillColorSecondaryBrush", ColorHelper.FromArgb(230, accent.R, accent.G, accent.B));
        UpdateAccentBrush(root, "AccentTextFillColorTertiaryBrush", ColorHelper.FromArgb(204, accent.R, accent.G, accent.B));
    }

    /// <summary>原地更新应用主题画刷，让已创建且持有画刷引用的原生控件立即更新。</summary>
    private static void UpdateAccentBrush(FrameworkElement root, string key, Color color)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        UpdateExistingThemeBrushes(resources, key, color, new(ReferenceEqualityComparer.Instance));
        var brush = resources.TryGetValue(key, out var value) && value is SolidColorBrush existing
            ? existing : new SolidColorBrush();
        brush.Color = color;
        resources[key] = brush;
        if (root.Resources.TryGetValue(key, out var oldValue) && oldValue is SolidColorBrush previous)
            previous.Color = color;
        root.Resources[key] = brush;
    }

    /// <summary>窗口主题可能与系统不同；同时更新所有已加载主题中的旧画刷引用。</summary>
    private static void UpdateExistingThemeBrushes(ResourceDictionary resources, string key, Color color,
        HashSet<ResourceDictionary> visited)
    {
        if (!visited.Add(resources)) return;
        if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            brush.Color = color;
        foreach (var merged in resources.MergedDictionaries)
            UpdateExistingThemeBrushes(merged, key, color, visited);
        foreach (var theme in resources.ThemeDictionaries.Values.OfType<ResourceDictionary>())
            UpdateExistingThemeBrushes(theme, key, color, visited);
    }

    private AppearanceApplyResult ApplyBackground(
        WindowAppearanceHost host,
        AppPreferences preferences)
    {
        var background = ResolveThemeBackground(host.ApplicationRoot, preferences.Theme);
        if (!preferences.CustomBackgroundEnabled)
        {
            host.BackgroundImage.Source = null;
            host.BackgroundImage.Visibility = Visibility.Collapsed;
            host.BackgroundOverlay.Background = preferences.Backdrop == BackdropMode.Solid
                ? new SolidColorBrush(background)
                : new SolidColorBrush(Colors.Transparent);
            return AppearanceApplyResult.Success();
        }

        var path = _backgroundAssets.ResolvePath(preferences.CustomBackgroundAssetName);
        if (path is null || !_backgroundAssets.Exists(preferences.CustomBackgroundAssetName))
        {
            ApplySolidFallback(host, preferences.Theme);
            return AppearanceApplyResult.Warning(Localize(
                preferences,
                "自定义背景图片不存在，已回退到纯色背景。",
                "The custom background image is missing; a solid background is being used."));
        }

        host.BackgroundImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
        host.BackgroundImage.Visibility = Visibility.Visible;
        var opacity = AppearanceOptionResolver.NormalizeOverlayOpacity(
            preferences.BackgroundOverlayOpacity);
        host.BackgroundOverlay.Background = new SolidColorBrush(ColorHelper.FromArgb(
            (byte)Math.Round(opacity * byte.MaxValue),
            background.R,
            background.G,
            background.B));
        return AppearanceApplyResult.Success();
    }

    private static void ApplySolidFallback(
        WindowAppearanceHost host,
        AppThemeMode theme)
    {
        host.BackgroundImage.Source = null;
        host.BackgroundImage.Visibility = Visibility.Collapsed;
        host.BackgroundOverlay.Background = new SolidColorBrush(
            ResolveThemeBackground(host.ApplicationRoot, theme));
    }

    private static ElementTheme ResolveTheme(AppThemeMode theme) => theme switch
    {
        AppThemeMode.Light => ElementTheme.Light,
        AppThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private static Color ResolveThemeBackground(
        FrameworkElement root,
        AppThemeMode theme)
    {
        var isDark = theme == AppThemeMode.Dark ||
                     theme == AppThemeMode.System && root.ActualTheme == ElementTheme.Dark;
        return isDark ? ColorHelper.FromArgb(255, 32, 32, 32) : Colors.White;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (value is not { Length: 9 } ||
            value[0] != '#' ||
            !uint.TryParse(
                value.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var argb))
        {
            return false;
        }

        color = ColorHelper.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
        return true;
    }

    private static string Localize(
        AppPreferences preferences,
        string chinese,
        string english) =>
        AppLanguageResolver.ResolveEnglish((int)preferences.Language) ? english : chinese;

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        // UISettings 可能从后台线程回调，窗口层负责调度回 UI 线程。
        SystemAppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        SystemAppearanceChanged = null;
    }
}
