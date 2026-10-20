using System.ComponentModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.Dialogs;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HyperKeyX.Features.Automation.Controls;

public sealed partial class MacroStepsControl : UserControl
{
    private MacroEditorViewModel? _subscribedEditor;
    private MousePathEditorViewModel? _pathEditor;
    private MacroEditorViewModel? _pathOwner;
    private bool _isRefreshingPropertyPane;
    private bool _isLoaded;

    public MacroStepsControl(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private MacroEditorViewModel Editor =>
        ViewModel.SelectedMacroEditor;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded)
            return;

        _isLoaded = true;
        ViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged += OnScaleMetricsChanged;
        AttachEditor();
        RefreshPropertyPane();
        UpdateResponsiveState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_isLoaded)
            return;

        ViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged -= OnScaleMetricsChanged;
        if (_subscribedEditor is not null)
            _subscribedEditor.PropertyChanged -= OnEditorPropertyChanged;
        _subscribedEditor = null;
        PathDetails.SetViewModel(null);
        _pathEditor?.Dispose();
        _pathEditor = null;
        _pathOwner = null;
        _isLoaded = false;
    }

    private void AttachEditor()
    {
        if (ReferenceEquals(_subscribedEditor, Editor))
            return;

        if (_subscribedEditor is not null)
            _subscribedEditor.PropertyChanged -= OnEditorPropertyChanged;
        _subscribedEditor = Editor;
        _subscribedEditor.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnMainViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsEnglish))
        {
            _pathEditor?.UpdateLocalization(ViewModel.IsEnglish);
            RefreshPropertyPane();
        }
        if (args.PropertyName != nameof(MainViewModel.SelectedMacroEditor))
            return;

        AttachEditor();
        Bindings.Update();
        RefreshPropertyPane();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MacroEditorViewModel.SelectedStep))
            RefreshPropertyPane();
    }

    private void OnScaleMetricsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.Settings.UiScaleMetrics.MacroEditorStackThreshold) or
            nameof(ViewModel.Settings.UiScaleMetrics.MacroEditorPropertyPaneWidth) or
            nameof(ViewModel.Settings.UiScaleMetrics.MacroEditorMinListWidth))
        {
            UpdateResponsiveState();
        }
    }

    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs args) =>
        UpdateResponsiveState();

    private async Task<KeyboardKeyIdentity?> PickKeyboardKeyAsync(
        KeyboardKeyIdentity? current = null)
    {
        var dialog = new KeyboardKeyPickerDialog(ViewModel.IsEnglish, current)
        {
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? dialog.SelectedKey
            : null;
    }

    private async void OnAddKeyboardClick(object sender, RoutedEventArgs args) =>
        await AddKeyboardAsync(isDown: null);

    private async void OnAddKeyboardDown(object sender, RoutedEventArgs args) =>
        await AddKeyboardAsync(isDown: true);

    private async void OnAddKeyboardUp(object sender, RoutedEventArgs args) =>
        await AddKeyboardAsync(isDown: false);

    private async Task AddKeyboardAsync(bool? isDown)
    {
        try
        {
            var selected = await PickKeyboardKeyAsync();
            if (selected is not KeyboardKeyIdentity key)
                return;

            ExecuteEditorAction(isDown switch
            {
                true => () => Editor.AddKeyboardStep(key, isDown: true),
                false => () => Editor.AddKeyboardStep(key, isDown: false),
                null => () => Editor.AddKeyboardClick(key)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MacroStepsControl] 选择宏按键失败：{ex}");
            Editor.ReportUnexpectedCommandError();
        }
    }

    private async void OnChooseSelectedKeyboardKey(
        object sender,
        RoutedEventArgs args)
    {
        var row = Editor.SelectedStep;
        if (row is null || !row.IsKeyboardAction)
            return;

        try
        {
            var current = KeyboardKeyIdentity.FromMacroStep(row.ToModel());
            var selected = await PickKeyboardKeyAsync(current);
            if (selected is not KeyboardKeyIdentity key)
                return;

            var model = row.ToModel();
            model.VirtualKeyCode = key.VirtualKeyCode;
            model.ScanCode = key.ScanCode;
            model.IsExtendedKey = key.IsExtendedKey;
            Editor.BeginSelectedStepEdit();
            Editor.PreviewSelectedStep(model);
            Editor.CommitSelectedStepEdit();
            RefreshPropertyPane();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MacroStepsControl] 选择宏按键失败：{ex}");
            Editor.CancelSelectedStepEdit();
            Editor.ReportUnexpectedCommandError();
            RefreshPropertyPane();
        }
    }

    private void OnAddMouseClick(object sender, RoutedEventArgs args) =>
        AddMouseFromTag(sender, isDown: null);

    private void OnAddMouseDown(object sender, RoutedEventArgs args) =>
        AddMouseFromTag(sender, isDown: true);

    private void OnAddMouseUp(object sender, RoutedEventArgs args) =>
        AddMouseFromTag(sender, isDown: false);

    private void AddMouseFromTag(object sender, bool? isDown)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !Enum.TryParse<MouseButtonKind>(tag, out var button))
        {
            Editor.ReportUnexpectedCommandError();
            return;
        }

        ExecuteEditorAction(isDown switch
        {
            true => () => Editor.AddMouseStep(button, isDown: true),
            false => () => Editor.AddMouseStep(button, isDown: false),
            null => () => Editor.AddMouseClick(button)
        });
    }

    private void OnAddDelay(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(() => Editor.AddDelay());

    private void OnAddMousePath(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Editor.AddManualPath);

    private void OnAddWheelUp(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(() => Editor.AddWheel(MacroEditorViewModel.DefaultWheelDelta));

    private void OnAddWheelDown(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(() => Editor.AddWheel(-MacroEditorViewModel.DefaultWheelDelta));

    private void OnUndo(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Undo);

    private void OnRedo(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Redo);

    private void OnMoveUp(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(() => Editor.MoveSelected(-1));

    private void OnMoveDown(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(() => Editor.MoveSelected(1));

    private void OnDuplicate(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Editor.DuplicateSelected);

    private void OnDelete(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Editor.DeleteSelected);

    private void OnRestoreSaved(object sender, RoutedEventArgs args) =>
        ExecuteEditorAction(Editor.RestoreSaved);

    private async void OnClear(object sender, RoutedEventArgs args)
    {
        if (Editor.Steps.Count == 0)
            return;

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Editor.ClearConfirmationTitle,
                Content = Editor.ClearConfirmationMessage,
                PrimaryButtonText = Editor.ConfirmClearText,
                CloseButtonText = Editor.CancelText,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                ExecuteEditorAction(Editor.Clear);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MacroStepsControl] 无法显示清空确认：{ex}");
            Editor.ReportUnexpectedCommandError();
        }
    }

    private void OnFocusFirstError(object sender, RoutedEventArgs args)
    {
        Editor.FocusFirstIssue();
        FocusSelectedStep();
        RefreshPropertyPane();
    }

    private void OnStepSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        RefreshPropertyPane();

    private void OnMouseButtonValueChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isRefreshingPropertyPane ||
            Editor.SelectedStep is not { IsMouseButtonAction: true } row ||
            MouseButtonValue.SelectedValue is not MouseButtonKind button)
        {
            return;
        }

        var model = row.ToModel();
        model.MouseButton = button;
        Editor.BeginSelectedStepEdit();
        Editor.PreviewSelectedStep(model);
        Editor.CommitSelectedStepEdit();
        RefreshPropertyPane();
    }

    private void OnNumericEditStarted(object sender, RoutedEventArgs args)
    {
        if (!_isRefreshingPropertyPane)
            Editor.BeginSelectedStepEdit();
    }

    private void OnDelayDurationChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args) =>
        PreviewNumericValue(args.NewValue, NumericField.Duration, requireDelay: true);

    /// <summary>启用随机时从原固定值建立等值区间，作为独立的可撤销事务。</summary>
    private void OnRandomDelayToggled(object sender, RoutedEventArgs args)
    {
        if (_isRefreshingPropertyPane || Editor.SelectedStep is not { CanConfigureDelay: true } row)
            return;
        Editor.CommitSelectedStepEdit();
        var model = row.ToModel();
        model.RandomDurationMaxMs = RandomDelayToggle.IsOn ? model.RandomDurationMaxMs ?? model.DurationMs : null;
        Editor.PreviewSelectedStep(model);
        Editor.CommitSelectedStepEdit();
        RefreshPropertyPane();
    }

    private void OnRandomDelayMaximumChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        PreviewNumericValue(args.NewValue, NumericField.MaximumDuration, requireDelay: false);

    /// <summary>分布选择独立提交，撤销时恢复原分布，保持延时区间不变。</summary>
    private void OnRandomDelayDistributionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isRefreshingPropertyPane || Editor.SelectedStep is not { IsRandomDelay: true } row ||
            RandomDelayDistributionValue.SelectedIndex is < 0 or > 1)
            return;
        Editor.CommitSelectedStepEdit();
        var model = row.ToModel();
        model.DelayDistribution = (MacroDelayDistribution)RandomDelayDistributionValue.SelectedIndex;
        Editor.PreviewSelectedStep(model);
        Editor.CommitSelectedStepEdit();
    }

    private void OnPostActionDelayChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args) =>
        PreviewNumericValue(args.NewValue, NumericField.Duration, requireDelay: false);

    private void OnWheelDeltaChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args) =>
        PreviewNumericValue(args.NewValue, NumericField.WheelDelta, requireDelay: false);

    private void PreviewNumericValue(
        double value,
        NumericField field,
        bool requireDelay)
    {
        if (_isRefreshingPropertyPane ||
            double.IsNaN(value) ||
            Editor.SelectedStep is not { } row ||
            (requireDelay && !row.IsDelayAction) ||
            (field == NumericField.WheelDelta && !row.IsWheelAction) ||
            (field == NumericField.MaximumDuration && !row.IsRandomDelay))
        {
            return;
        }

        var model = row.ToModel();
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (field == NumericField.Duration)
            model.DurationMs = rounded;
        else if (field == NumericField.MaximumDuration)
            model.RandomDurationMaxMs = rounded;
        else
            model.WheelDelta = rounded;

        Editor.BeginSelectedStepEdit();
        Editor.PreviewSelectedStep(model);
    }

    private void OnNumericEditCompleted(object sender, RoutedEventArgs args)
    {
        if (!_isRefreshingPropertyPane)
            Editor.CommitSelectedStepEdit();
    }

    private void OnNumericEditKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            Editor.CommitSelectedStepEdit();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            Editor.CancelSelectedStepEdit();
            RefreshPropertyPane();
            args.Handled = true;
        }
    }

    private void RefreshPropertyPane()
    {
        if (DelayDurationValue is null)
            return;

        _isRefreshingPropertyPane = true;
        try
        {
            var row = Editor.SelectedStep;
            if (!ReferenceEquals(_pathOwner, Editor) || _pathEditor?.StepId != (row?.IsPathAction == true ? row.StepId : null))
            {
                PathDetails.SetViewModel(null);
                _pathEditor?.Dispose();
                _pathOwner = Editor;
                _pathEditor = row?.IsPathAction == true ? new(Editor, row.StepId, ViewModel.IsEnglish) : null;
                PathDetails.SetViewModel(_pathEditor);
            }
            PropertyPane.DataContext = row;
            RandomDelayToggle.IsOn = row?.IsRandomDelay == true;
            RandomDelayMaximumValue.Value = row?.RandomDurationMaxMs ?? 0;
            RandomDelayDistributionValue.ItemsSource = new[] { Editor.UniformDelayText, Editor.NormalDelayText };
            RandomDelayDistributionValue.SelectedIndex = (int)(row?.DelayDistribution ?? MacroDelayDistribution.Uniform);
            DelayDurationValue.Value = row?.IsDelayAction == true
                ? row.DurationMs
                : 0;
            PostActionDelayValue.Value = row?.HasPostActionDelay == true
                ? row.DurationMs
                : 0;
            WheelDeltaValue.Value = row?.IsWheelAction == true
                ? row.WheelDelta
                : 0;
            RefreshMouseButtonChoices();
        }
        finally
        {
            _isRefreshingPropertyPane = false;
        }
    }

    private void RefreshMouseButtonChoices()
    {
        MouseButtonValue.ItemsSource = new[]
        {
            new MouseButtonChoice(MouseButtonKind.Left, Editor.LeftMouseButtonText),
            new MouseButtonChoice(MouseButtonKind.Right, Editor.RightMouseButtonText),
            new MouseButtonChoice(MouseButtonKind.Middle, Editor.MiddleMouseButtonText),
            new MouseButtonChoice(MouseButtonKind.XButton1, Editor.MouseButton4Text),
            new MouseButtonChoice(MouseButtonKind.XButton2, Editor.MouseButton5Text)
        };
        MouseButtonValue.DisplayMemberPath = nameof(MouseButtonChoice.DisplayName);
        MouseButtonValue.SelectedValuePath = nameof(MouseButtonChoice.Value);
        MouseButtonValue.SelectedValue = Editor.SelectedStep?.MouseButton;
    }

    private void UpdateResponsiveState()
    {
        if (EditorBody is null)
            return;

        var metrics = ViewModel.Settings.UiScaleMetrics;
        var compact = ActualWidth < metrics.MacroEditorStackThreshold;
        VisualStateManager.GoToState(
            this,
            compact ? "CompactEditorState" : "WideEditorState",
            useTransitions: false);

        EditorBody.RowDefinitions.Clear();
        EditorBody.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        if (compact)
        {
            EditorBody.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(12)
            });
            EditorBody.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
        }

        Grid.SetRow(MacroListHost, 0);
        Grid.SetColumn(MacroListHost, 0);
        Grid.SetColumnSpan(MacroListHost, compact ? 3 : 1);
        MacroListHost.MinWidth = compact ? 0 : metrics.MacroEditorMinListWidth;
        Grid.SetColumn(PropertyPane, compact ? 0 : 2);
        Grid.SetRow(PropertyPane, compact ? 2 : 0);
        Grid.SetColumnSpan(PropertyPane, compact ? 3 : 1);
        PropertyPane.Width = compact
            ? double.NaN
            : metrics.MacroEditorPropertyPaneWidth;
        PropertyPane.Padding = compact
            ? new Thickness(0, 16, 0, 8)
            : new Thickness(16, 8, 0, 8);
    }

    private bool ExecuteEditorAction(Func<bool> action)
    {
        try
        {
            var changed = action();
            if (changed)
                FocusSelectedStep();
            RefreshPropertyPane();
            return changed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MacroStepsControl] 宏编辑操作失败：{ex}");
            Editor.CancelSelectedStepEdit();
            Editor.ReportUnexpectedCommandError();
            RefreshPropertyPane();
            return false;
        }
    }

    private bool Undo()
    {
        if (!Editor.CanUndo)
            return false;
        Editor.Undo();
        return true;
    }

    private bool Redo()
    {
        if (!Editor.CanRedo)
            return false;
        Editor.Redo();
        return true;
    }

    private void FocusSelectedStep()
    {
        if (Editor.SelectedStep is not { } row)
            return;

        MacroStepList.ScrollIntoView(row);
        if (MacroStepList.ContainerFromItem(row) is ListViewItem container)
            container.Focus(FocusState.Keyboard);
    }

    private void OnUndoAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(Undo);

    private void OnRedoAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(Redo);

    private void OnDeleteAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(Editor.DeleteSelected);

    private void OnDuplicateAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(Editor.DuplicateSelected);

    private void OnMoveUpAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(() => Editor.MoveSelected(-1));

    private void OnMoveDownAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = ExecuteEditorAction(() => Editor.MoveSelected(1));

    private sealed record MouseButtonChoice(
        MouseButtonKind Value,
        string DisplayName);

    private enum NumericField
    {
        Duration,
        MaximumDuration,
        WheelDelta
    }
}
