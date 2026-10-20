using System.ComponentModel;
using HyperKeyX.Features.Automation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace HyperKeyX.Features.Automation.Controls;

public sealed partial class MousePathEditorControl : UserControl
{
    private MousePathEditorViewModel? _viewModel;
    private IReadOnlyList<HyperKeyX.Domain.Automation.Paths.PathPosition>? _drawnPoints;
    private IReadOnlyList<HyperKeyX.Domain.Automation.Paths.PathPosition>? _drawnRawPoints;
    public MousePathEditorControl() => InitializeComponent();

    public void SetViewModel(MousePathEditorViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnModelChanged;
        _viewModel = viewModel;
        Details.DataContext = viewModel;
        if (viewModel is not null) viewModel.PropertyChanged += OnModelChanged;
        DrawPreview();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!ReferenceEquals(_drawnPoints, _viewModel?.PreviewPoints) || !ReferenceEquals(_drawnRawPoints, _viewModel?.RawPreviewPoints)) DrawPreview();
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs args) => DrawPreview();
    private void DrawPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        _drawnPoints = _viewModel?.PreviewPoints;
        _drawnRawPoints = _viewModel?.RawPreviewPoints;
        var points = (_drawnPoints ?? []).Concat(_drawnRawPoints ?? []).ToArray();
        if (points.Length == 0 || PreviewCanvas.ActualWidth < 20 || PreviewCanvas.ActualHeight < 20) return;
        var minX = points.Min(p => p.X); var minY = points.Min(p => p.Y);
        var width = Math.Max(1, points.Max(p => p.X) - minX);
        var height = Math.Max(1, points.Max(p => p.Y) - minY);
        var scale = Math.Min((PreviewCanvas.ActualWidth - 16) / width, (PreviewCanvas.ActualHeight - 16) / height);
        var offsetX = (PreviewCanvas.ActualWidth - width * scale) / 2;
        var offsetY = (PreviewCanvas.ActualHeight - height * scale) / 2;
        var brush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemControlHighlightAccentBrush"];
        var rawLine = new Polyline { Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray), StrokeThickness = 1, Opacity = .7 };
        foreach (var point in _drawnRawPoints ?? []) rawLine.Points.Add(new Point(offsetX + (point.X - minX) * scale, offsetY + (point.Y - minY) * scale));
        PreviewCanvas.Children.Add(rawLine);
        var line = new Polyline { Stroke = brush, StrokeThickness = 2 };
        foreach (var point in _drawnPoints ?? []) line.Points.Add(new Point(offsetX + (point.X - minX) * scale, offsetY + (point.Y - minY) * scale));
        PreviewCanvas.Children.Add(line);
        if (line.Points.Count == 0) return;
        foreach (var point in new[] { line.Points[0], line.Points[^1] })
        {
            var marker = new Ellipse { Width = 7, Height = 7, Fill = brush };
            Canvas.SetLeft(marker, point.X - 3.5); Canvas.SetTop(marker, point.Y - 3.5);
            PreviewCanvas.Children.Add(marker);
        }
    }

    private void OnEditStarted(object sender, RoutedEventArgs args) => _viewModel?.BeginGesture();
    private void OnEditCompleted(object sender, RoutedEventArgs args) => _viewModel?.EndGesture();
    private void OnEditKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape) { _viewModel?.CancelGesture(); args.Handled = true; }
        else if (args.Key == VirtualKey.Enter) { _viewModel?.EndGesture(); args.Handled = true; }
    }
    private void OnAddPoint(object sender, RoutedEventArgs args) => _viewModel?.AddPoint();
    private void OnDeletePoint(object sender, RoutedEventArgs args) => _viewModel?.DeletePoint();
    private void OnRestoreSource(object sender, RoutedEventArgs args) => _viewModel?.RestoreSource();
    private void OnPrevious(object sender, RoutedEventArgs args) => _viewModel?.PreviousPage();
    private void OnNext(object sender, RoutedEventArgs args) => _viewModel?.NextPage();
    private void OnAddTarget(object sender, RoutedEventArgs args) => _viewModel?.AddTarget();
    private void OnRemoveTarget(object sender, RoutedEventArgs args) => _viewModel?.RemoveTarget();
    private void OnLocateIssue(object sender, RoutedEventArgs args)
    {
        _viewModel?.GoToFirstIssue();
        if (_viewModel?.SelectedPoint is { } point) PointList.ScrollIntoView(point);
    }
}
