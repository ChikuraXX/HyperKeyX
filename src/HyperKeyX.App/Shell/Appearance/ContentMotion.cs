using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace HyperKeyX.Shell.Appearance;

/// <summary>为选中上下文变化提供短促过渡，不替换控件、不等待动画后再执行操作。</summary>
public static class ContentMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement, MotionState> States = new();

    /// <summary>绑定页面或选中对象标识；对象内部文本编辑不会触发此过渡。</summary>
    public static readonly DependencyProperty ContextProperty = DependencyProperty.RegisterAttached(
        "Context", typeof(object), typeof(ContentMotion), new PropertyMetadata(null, OnContextChanged));

    public static object? GetContext(DependencyObject element) => element.GetValue(ContextProperty);
    public static void SetContext(DependencyObject element, object? value) => element.SetValue(ContextProperty, value);

    private static void OnContextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement host)
            return;
        var state = States.GetValue(host, element =>
        {
            element.Unloaded += OnUnloaded;
            return new MotionState();
        });
        var version = ++state.Version;
        if (!host.IsLoaded)
            return;
        // 让绑定和 Visibility 先应用；连续选择只播放最后一次，避免排队闪烁。
        host.DispatcherQueue.TryEnqueue(() =>
        {
            if (state.Version != version || !host.IsLoaded)
                return;
            Reset(state);
            if (!state.Settings.AnimationsEnabled || !IsVisible(host))
                return;
            ElementCompositionPreview.SetIsTranslationEnabled(host, true);
            var visual = ElementCompositionPreview.GetElementVisual(host);
            state.Visual = visual;
            var compositor = visual.Compositor;
            using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));
            using var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0, new Vector3(0, 8, 0));
            move.InsertKeyFrame(1, Vector3.Zero, easing);
            move.Duration = TimeSpan.FromMilliseconds(180);
            using var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0.35f);
            fade.InsertKeyFrame(1, 1, easing);
            fade.Duration = move.Duration;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Opacity", fade);
        });
    }

    private static bool IsVisible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null;
             current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
                return false;
        }
        return true;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement host && States.TryGetValue(host, out var state))
        {
            state.Version++;
            Reset(state);
        }
    }

    private static void Reset(MotionState state)
    {
        if (state.Visual is not { } visual)
            return;
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
        state.Visual = null;
    }

    /// <summary>弱关联到动画宿主；不订阅外部状态，不保留已卸载页面。</summary>
    private sealed class MotionState
    {
        public long Version { get; set; }
        public UISettings Settings { get; } = new();
        public Visual? Visual { get; set; }
    }
}
