using System.ComponentModel;
using System.Globalization;
using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Application.Appearance;
using HyperKeyX.Domain.Appearance;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace HyperKeyX.Features.Settings.Views;

/// <summary>
/// 提供界面外观选择，并把本地文件选择结果交给设置状态管理。
/// </summary>
public sealed partial class AppearanceSettingsView : UserControl
{
    private bool _isSynchronizingAccentPicker;
    private bool _isPickingBackground;
    private bool _isViewModelSubscriptionAttached;
    private bool _isChoosingFont;

    public AppearanceSettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateAccentSwatch();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>在独立选择器内筛选、预览字体；只有确认才修改偏好。</summary>
    private async void OnChooseFont(object sender, RoutedEventArgs e)
    {
        if (_isChoosingFont)
            return;
        _isChoosingFont = true;
        ChooseFontButton.IsEnabled = false;
        try
        {
            var fonts = await ViewModel.LoadInstalledFontsAsync();
            if (!IsLoaded)
                return;
            string L(string chinese, string english) => ViewModel.IsEnglish ? english : chinese;
            var system = new InstalledFontFamily("", L("跟随系统", "Use system setting"), [], []);
            var choices = new[] { system }.Concat(fonts).ToArray();
            var groups = new[] { new FontLanguageCoverage.Group("", "全部字体", "All fonts", "Aa 中文 123") }
                .Concat(FontLanguageCoverage.Groups)
                .Append(new FontLanguageCoverage.Group("other", "其他／符号", "Other / symbols", "Aa 123"))
                .ToArray();
            var language = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            language.ItemsSource = groups.Select(group => L(group.ChineseName, group.EnglishName)).ToArray();
            language.SelectedIndex = 0;
            AutomationProperties.SetName(language, L("字体语言分类", "Font language"));
            var search = new TextBox { PlaceholderText = L("搜索字体名称", "Search font names") };
            AutomationProperties.SetName(search, search.PlaceholderText);
            var list = new ListView { Height = 260, SelectionMode = ListViewSelectionMode.Single,
                DisplayMemberPath = nameof(InstalledFontFamily.DisplayName) };
            AutomationProperties.SetName(list, L("已安装字体", "Installed fonts"));
            var preview = new TextBlock { Text = groups[0].Sample, FontSize = 22,
                TextWrapping = TextWrapping.Wrap, MaxHeight = 80 };
            var count = new TextBlock();
            var content = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 520 };
            content.Children.Add(language);
            content.Children.Add(search);
            content.Children.Add(count);
            content.Children.Add(list);
            content.Children.Add(preview);
            ToolTipService.SetToolTip(language, L("按代表字符覆盖分类，不保证覆盖该语言的全部字符。",
                "Groups use representative glyph coverage, not complete language coverage."));
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = L("选择字体", "Choose font"), Content = content,
                PrimaryButtonText = L("应用", "Apply"), CloseButtonText = L("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false
            };
            InstalledFontFamily? selected = choices.FirstOrDefault(font => string.Equals(font.FamilyName,
                ViewModel.CurrentPreferences.FontFamily ?? AppearanceOptionResolver.FontFamilyName(ViewModel.CurrentPreferences.Font),
                StringComparison.OrdinalIgnoreCase));
            if (ViewModel.CurrentPreferences.FontFamily is null && ViewModel.CurrentPreferences.Font == Domain.Preferences.AppFontOption.System)
                selected = system;
            void Filter()
            {
                var group = groups[Math.Max(0, language.SelectedIndex)];
                var filtered = choices.Where(font => font.Matches(group.Id, search.Text)).ToArray();
                list.ItemsSource = filtered;
                list.SelectedItem = selected is not null && filtered.Contains(selected) ? selected : null;
                preview.Text = group.Sample;
                count.Text = L($"{filtered.Length} 项", $"{filtered.Length} items");
                dialog.IsPrimaryButtonEnabled = list.SelectedItem is InstalledFontFamily;
            }
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is InstalledFontFamily font)
                {
                    selected = font;
                    preview.FontFamily = new FontFamily(string.IsNullOrEmpty(font.FamilyName)
                        ? "Segoe UI Variable Text" : font.FamilyName);
                }
                dialog.IsPrimaryButtonEnabled = list.SelectedItem is InstalledFontFamily;
            };
            search.TextChanged += (_, _) => Filter();
            language.SelectionChanged += (_, _) => Filter();
            Filter();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is InstalledFontFamily chosen)
                ViewModel.SelectInstalledFont(string.IsNullOrEmpty(chosen.FamilyName) ? null : chosen.FamilyName);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[AppearanceSettingsView] 枚举或选择字体失败: {exception}");
            ViewModel.ShowWarning(ViewModel.IsEnglish ? "Could not load installed fonts." : "无法加载已安装字体。");
        }
        finally
        {
            _isChoosingFont = false;
            ChooseFontButton.IsEnabled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isViewModelSubscriptionAttached)
            return;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isViewModelSubscriptionAttached = true;
        UpdateAccentSwatch();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isViewModelSubscriptionAttached)
            return;

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isViewModelSubscriptionAttached = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.CustomAccentColor))
            UpdateAccentSwatch();
    }

    private void OnAccentFlyoutOpened(object sender, object e)
    {
        if (!TryParseColor(ViewModel.CustomAccentColor, out var color))
            return;

        _isSynchronizingAccentPicker = true;
        try
        {
            AccentColorPicker.Color = color;
        }
        finally
        {
            _isSynchronizingAccentPicker = false;
        }
    }

    private void OnAccentColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_isSynchronizingAccentPicker)
            return;

        var color = args.NewColor;
        ViewModel.CustomAccentColor =
            $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        UpdateAccentSwatch();
    }

    private async void OnChooseBackground(object sender, RoutedEventArgs e)
    {
        if (_isPickingBackground || sender is not Button button)
            return;

        _isPickingBackground = true;
        button.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            var windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, windowHandle);

            if (await picker.PickSingleFileAsync() is { } file)
                await ViewModel.ImportBackgroundAsync(file.Path);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AppearanceSettingsView] 选择背景图片失败: {exception}");
            ViewModel.ShowWarning(ViewModel.IsEnglish
                ? "Could not open the local image picker."
                : "无法打开本地图片选择器。");
        }
        finally
        {
            button.IsEnabled = true;
            _isPickingBackground = false;
        }
    }

    private void OnOpacityInteractionCompleted(
        object sender,
        PointerRoutedEventArgs e) =>
        ViewModel.CommitBackgroundOverlayOpacity();

    private void OnOpacityLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.CommitBackgroundOverlayOpacity();

    private void OnOpacitySliderKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Left or
            VirtualKey.Right or
            VirtualKey.Up or
            VirtualKey.Down or
            VirtualKey.Home or
            VirtualKey.End or
            VirtualKey.PageUp or
            VirtualKey.PageDown or
            VirtualKey.Enter or
            VirtualKey.Space)
        {
            ViewModel.CommitBackgroundOverlayOpacity();
        }
    }

    private void UpdateAccentSwatch()
    {
        if (TryParseColor(ViewModel.CustomAccentColor, out var color))
            AccentSwatch.Background = new SolidColorBrush(color);
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
}
