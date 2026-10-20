using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Preferences;
using Microsoft.UI.Xaml;

namespace HyperKeyX.Features.Settings.ViewModels;

/// <summary>
/// 将离散界面缩放预设转换为可绑定且参与布局测量的度量。
/// </summary>
public sealed class UiScaleMetrics : ObservableObject
{
    private UiScaleMode _mode;
    private double _factor;

    public UiScaleMetrics(UiScaleMode mode)
    {
        _mode = Normalize(mode);
        _factor = ResolveFactor(_mode);
    }

    public UiScaleMode Mode => _mode;
    public double Factor => _factor;
    public Thickness KeyboardPagePadding => new(12 * Factor, 10 * Factor, 12 * Factor, 16 * Factor);
    public Thickness StandardPageMargin => new(28 * Factor, 22 * Factor, 28 * Factor, 28 * Factor);
    /// <summary>分类与列表选项随文字缩放的最小点击高度。</summary>
    public double SelectionItemMinHeight => 40 * Factor;
    /// <summary>设置分类项的水平和垂直内边距。</summary>
    public Thickness SelectionItemPadding => new(12 * Factor, 8 * Factor, 12 * Factor, 8 * Factor);
    public double SectionSpacing => 12 * Factor;
    public double KeyboardCanvasWidth => 920 * Factor;
    /// <summary>保证左右布局下键盘至少获得设计宽度约 85%；不足时将检查器置于下方。</summary>
    public bool ShouldStackKeyboard(double availableWidth) =>
        availableWidth < 780 * Factor + EditorPanelWidth + SectionSpacing;
    /// <summary>鼠标按钮至少容纳两列，否则把参数检查器放到下一行。</summary>
    public bool ShouldStackMouse(double availableWidth) =>
        availableWidth < 2 * MouseSelectorMinWidth + 32 + 2 * SectionSpacing + EditorPanelWidth;

    /// <summary>按内容区宽度判断宏库双栏是否能容纳列表、间隔与详情。</summary>
    public bool CanShowMacroLibraryColumns(double availableWidth) =>
        availableWidth >= Math.Max(MacroLibraryStackThreshold,
            MacroLibraryListWidth + 12 + MacroLibraryDetailMinWidth);

    public double EditorPanelWidth => Math.Min(340 * Factor, 460);
    public double DurationInputMinWidth => Math.Min(128 * Factor, 192);
    public double MouseSelectorMinWidth => Math.Min(128 * Factor, 184);
    public double MouseSelectorHeight => 84 * Factor;
    public double SmallIconButtonSize => 28 * Factor;
    /// <summary>存储等紧凑操作按钮随图标一同缩放。</summary>
    public double CompactActionButtonSize => 32 * Factor;
    public double StandardIconButtonSize => 36 * Factor;
    public double IconGlyphSize => 16 * Factor;
    /// <summary>各层级文字和图标共用缩放因子，避免页面保留固定字号。</summary>
    public double SmallCaptionFontSize => 11 * Factor;
    public double CompactBodyFontSize => 13 * Factor;
    public double SubtitleFontSize => 15 * Factor;
    public double SectionFontSize => 16 * Factor;
    public double HeadingFontSize => 18 * Factor;
    public double LargeIconGlyphSize => 20 * Factor;
    public double PageTitleFontSize => 22 * Factor;
    public double LargeTitleFontSize => 24 * Factor;
    public double DisplayIconGlyphSize => 28 * Factor;
    /// <summary>为两个底部导航项及模板留白保留足够高度。</summary>
    public double NavigationFooterMinHeight => 2 * (NavigationItemHeight + NavigationItemMargin.Top + NavigationItemMargin.Bottom) + 16;
    public double BodyFontSize => 14 * Factor;
    public double CaptionFontSize => 12 * Factor;
    public double SettingsRowMinHeight => 68 * Factor;
    public double SettingsActionWidth => Math.Min(220 * Factor, 320);
    public double ProfileListWidth => Math.Min(320 * Factor, 420);
    public double NavigationOpenPaneLength => Math.Min(216 * Factor, 300);
    public double NavigationCompactPaneLength => 56 * Factor;
    /// <summary>导航项使用统一点击高度和对称间距，与图标、紧凑栏宽同比缩放。</summary>
    public double NavigationItemHeight => 48 * Factor;
    /// <summary>每个导航项上下各留四个基准逻辑像素，保证菜单项间距一致。</summary>
    public Thickness NavigationItemMargin => new(0, 4 * Factor, 0, 4 * Factor);
    /// <summary>常规桌面宽度优先展示文字；窄窗口或高缩放时切换紧凑导航。</summary>
    public double NavigationCollapseWidth => 1100 * Factor;
    public double ProfilesWideWidth => 900 * Factor;
    public double SettingsRowWideWidth => 640 * Factor;
    public double MacroEditorPropertyPaneWidth => Math.Min(320 * Factor, 420);
    public double MacroEditorMinListWidth => Math.Min(360 * Factor, 520);
    public double MacroEditorStackThreshold => Mode switch
    {
        UiScaleMode.Percent150 => 1_040,
        UiScaleMode.Percent175 or UiScaleMode.Percent200 => 1_180,
        UiScaleMode.Percent125 => 900,
        _ => 760
    };
    public double MacroNumericInputMinWidth => Math.Min(156 * Factor, 220);
    public double MacroEditorRowMinHeight => Math.Min(40 * Factor, 56);
    public double MacroLibraryListWidth => Math.Min(300 * Factor, 420);
    public double MacroLibraryDetailMinWidth => Math.Min(520 * Factor, 680);
    public double MacroLibraryStackThreshold => Mode switch
    {
        UiScaleMode.Percent150 => 1_100,
        UiScaleMode.Percent175 => 1_180,
        UiScaleMode.Percent200 => 1_260,
        UiScaleMode.Percent125 => 1_000,
        _ => 900
    };

    public void Apply(UiScaleMode mode)
    {
        var normalized = Normalize(mode);
        var factor = ResolveFactor(normalized);
        if (_mode == normalized && Math.Abs(_factor - factor) < double.Epsilon)
            return;

        _mode = normalized;
        _factor = factor;
        RaiseMetricChanges();
    }

    private void RaiseMetricChanges()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Factor));
        OnPropertyChanged(nameof(KeyboardPagePadding));
        OnPropertyChanged(nameof(StandardPageMargin));
        OnPropertyChanged(nameof(SelectionItemMinHeight));
        OnPropertyChanged(nameof(SelectionItemPadding));
        OnPropertyChanged(nameof(SectionSpacing));
        OnPropertyChanged(nameof(KeyboardCanvasWidth));
        OnPropertyChanged(nameof(EditorPanelWidth));
        OnPropertyChanged(nameof(DurationInputMinWidth));
        OnPropertyChanged(nameof(MouseSelectorMinWidth));
        OnPropertyChanged(nameof(MouseSelectorHeight));
        OnPropertyChanged(nameof(SmallIconButtonSize));
        OnPropertyChanged(nameof(CompactActionButtonSize));
        OnPropertyChanged(nameof(StandardIconButtonSize));
        OnPropertyChanged(nameof(IconGlyphSize));
        OnPropertyChanged(nameof(NavigationFooterMinHeight));
        OnPropertyChanged(nameof(SmallCaptionFontSize));
        OnPropertyChanged(nameof(CompactBodyFontSize));
        OnPropertyChanged(nameof(SubtitleFontSize));
        OnPropertyChanged(nameof(SectionFontSize));
        OnPropertyChanged(nameof(HeadingFontSize));
        OnPropertyChanged(nameof(LargeIconGlyphSize));
        OnPropertyChanged(nameof(PageTitleFontSize));
        OnPropertyChanged(nameof(LargeTitleFontSize));
        OnPropertyChanged(nameof(DisplayIconGlyphSize));
        OnPropertyChanged(nameof(BodyFontSize));
        OnPropertyChanged(nameof(CaptionFontSize));
        OnPropertyChanged(nameof(SettingsRowMinHeight));
        OnPropertyChanged(nameof(SettingsActionWidth));
        OnPropertyChanged(nameof(ProfileListWidth));
        OnPropertyChanged(nameof(NavigationOpenPaneLength));
        OnPropertyChanged(nameof(NavigationCompactPaneLength));
        OnPropertyChanged(nameof(NavigationItemHeight));
        OnPropertyChanged(nameof(NavigationItemMargin));
        OnPropertyChanged(nameof(NavigationCollapseWidth));
        OnPropertyChanged(nameof(ProfilesWideWidth));
        OnPropertyChanged(nameof(SettingsRowWideWidth));
        OnPropertyChanged(nameof(MacroEditorPropertyPaneWidth));
        OnPropertyChanged(nameof(MacroEditorMinListWidth));
        OnPropertyChanged(nameof(MacroEditorStackThreshold));
        OnPropertyChanged(nameof(MacroNumericInputMinWidth));
        OnPropertyChanged(nameof(MacroEditorRowMinHeight));
        OnPropertyChanged(nameof(MacroLibraryListWidth));
        OnPropertyChanged(nameof(MacroLibraryDetailMinWidth));
        OnPropertyChanged(nameof(MacroLibraryStackThreshold));
    }

    private static UiScaleMode Normalize(UiScaleMode mode) =>
        Enum.IsDefined(mode) ? mode : UiScaleMode.System;

    private static double ResolveFactor(UiScaleMode mode) => mode switch
    {
        UiScaleMode.Percent125 => 1.25,
        UiScaleMode.Percent150 => 1.5,
        UiScaleMode.Percent175 => 1.75,
        UiScaleMode.Percent200 => 2.0,
        _ => 1.0
    };
}
