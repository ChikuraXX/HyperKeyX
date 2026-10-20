using HyperKeyX.Domain.Preferences;
using HyperKeyX.Features.Settings.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Settings.Controls;

/// <summary>
/// 以稳定三列布局承载设置说明和操作控件。
/// </summary>
public sealed partial class SettingRow : UserControl
{
    private static readonly UiScaleMetrics FallbackScaleMetrics =
        new(UiScaleMode.System);

    private UiScaleMetrics? _subscribedScaleMetrics;
    private bool _isLoaded;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(SettingRow),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent),
        typeof(object),
        typeof(SettingRow),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ScaleMetricsProperty = DependencyProperty.Register(
        nameof(ScaleMetrics),
        typeof(UiScaleMetrics),
        typeof(SettingRow),
        new PropertyMetadata(null, OnScaleMetricsChanged));

    public SettingRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public UiScaleMetrics ScaleMetrics
    {
        get => (UiScaleMetrics?)GetValue(ScaleMetricsProperty) ?? FallbackScaleMetrics;
        set => SetValue(ScaleMetricsProperty, value);
    }

    private static void OnScaleMetricsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var row = (SettingRow)dependencyObject;
        row.UpdateScaleSubscription();
        row.UpdateResponsiveState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateScaleSubscription();
        UpdateResponsiveState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        DetachScaleSubscription();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveState();

    private void OnScaleMetricsPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e) =>
        UpdateResponsiveState();

    private void UpdateScaleSubscription()
    {
        if (!_isLoaded || ReferenceEquals(_subscribedScaleMetrics, ScaleMetrics))
            return;

        DetachScaleSubscription();
        _subscribedScaleMetrics = ScaleMetrics;
        _subscribedScaleMetrics.PropertyChanged += OnScaleMetricsPropertyChanged;
    }

    private void DetachScaleSubscription()
    {
        if (_subscribedScaleMetrics is null)
            return;

        _subscribedScaleMetrics.PropertyChanged -= OnScaleMetricsPropertyChanged;
        _subscribedScaleMetrics = null;
    }

    private void UpdateResponsiveState()
    {
        var threshold = ScaleMetrics.SettingsRowWideWidth;
        var state = ActualWidth >= threshold ? "WideLayout" : "NarrowLayout";
        _ = VisualStateManager.GoToState(this, state, false);
    }
}
