using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Features.Automation.Controls;
using HyperKeyX.Features.Macros.Services;
using HyperKeyX.Features.Macros.ViewModels;
using HyperKeyX.Shell.ViewModels;
using HyperKeyX.Shell.Files;
using HyperKeyX.Application.Common;
using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HyperKeyX.Features.Macros.Views;

/// <summary>
/// 承载应用级宏库，并在窄窗口或高应用缩放下切换为单栏导航。
/// </summary>
public sealed partial class MacroLibraryPage : Page
{
    private MacroLibraryViewModel? _subscribedLibrary;
    private bool _isLoaded;
    private bool _isWideLayout;
    private bool _showingDetail;
    private string? _currentLayoutState;

    public MacroLibraryPage(MainViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ImportMacroCommand = new AsyncRelayCommand(ImportMacroAsync);
        ExportCurrentMacroCommand = new AsyncRelayCommand(ExportCurrentMacroAsync);
        DeleteCurrentMacroCommand = new AsyncRelayCommand(DeleteCurrentMacroAsync);

        InitializeComponent();
        MacroEditorHost.Content = new MacroStepsControl(viewModel);
    }

    public MainViewModel ViewModel { get; }
    public IAsyncRelayCommand ImportMacroCommand { get; }
    public IAsyncRelayCommand ExportCurrentMacroCommand { get; }
    public IAsyncRelayCommand DeleteCurrentMacroCommand { get; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded)
            return;

        _isLoaded = true;
        ViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged += OnScaleMetricsChanged;
        AttachLibrary(ViewModel.MacroLibrary);
        UpdateResponsiveState();
        UpdateContentStates();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_isLoaded)
            return;

        ViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        ViewModel.Settings.UiScaleMetrics.PropertyChanged -= OnScaleMetricsChanged;
        DetachLibrary();
        _isLoaded = false;
    }

    private void AttachLibrary(MacroLibraryViewModel library)
    {
        if (ReferenceEquals(_subscribedLibrary, library))
            return;

        DetachLibrary();
        _subscribedLibrary = library;
        library.PropertyChanged += OnMacroLibraryPropertyChanged;
        library.FilteredMacros.CollectionChanged += OnMacroCollectionChanged;
        library.SelectedMacroReferences.CollectionChanged += OnReferenceCollectionChanged;
        library.BlockingReferences.CollectionChanged += OnReferenceCollectionChanged;
    }

    private void DetachLibrary()
    {
        if (_subscribedLibrary is null)
            return;

        _subscribedLibrary.PropertyChanged -= OnMacroLibraryPropertyChanged;
        _subscribedLibrary.FilteredMacros.CollectionChanged -= OnMacroCollectionChanged;
        _subscribedLibrary.SelectedMacroReferences.CollectionChanged -= OnReferenceCollectionChanged;
        _subscribedLibrary.BlockingReferences.CollectionChanged -= OnReferenceCollectionChanged;
        _subscribedLibrary = null;
    }

    private void OnMainViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.MacroLibrary))
        {
            AttachLibrary(ViewModel.MacroLibrary);
            Bindings.Update();
        }

        if (args.PropertyName is nameof(MainViewModel.MacroLibrary) or
            nameof(MainViewModel.HasSelectedMacro) or
            nameof(MainViewModel.IsEnglish))
        {
            UpdateContentStates();
        }
    }

    private void OnMacroLibraryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MacroLibraryViewModel.SelectedMacro))
            return;

        if (!_isWideLayout && ViewModel.MacroLibrary.SelectedMacro is not null)
            _showingDetail = true;

        Bindings.Update();
        UpdateResponsiveState();
        UpdateContentStates();
    }

    private void OnMacroCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args) => UpdateContentStates();

    private void OnReferenceCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args) => UpdateContentStates();

    private void OnScaleMetricsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.Settings.UiScaleMetrics.MacroLibraryListWidth) or
            nameof(ViewModel.Settings.UiScaleMetrics.MacroLibraryDetailMinWidth) or
            nameof(ViewModel.Settings.UiScaleMetrics.MacroLibraryStackThreshold))
        {
            UpdateResponsiveState();
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args) =>
        UpdateResponsiveState();

    private void OnMacroSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_isWideLayout && MacroList.SelectedItem is not null)
        {
            _showingDetail = true;
            UpdateResponsiveState();
        }

        UpdateContentStates();
    }

    private void OnBackToMacroList(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsRecordingSessionActive)
            return;

        _showingDetail = false;
        UpdateResponsiveState();
        MacroList.Focus(FocusState.Programmatic);
    }

    private async void OnToggleRecording(object sender, RoutedEventArgs args) =>
        await ViewModel.ToggleRecordingAsync();

    private void OnReferencesOpened(object sender, object args) => UpdateContentStates();

    private void OnOpenMacroReferenceCommand(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: MacroReferenceViewModel reference })
        {
            MacroReferencesFlyout.Hide();
            ViewModel.OpenMacroReferenceCommand.Execute(reference);
        }
    }

    private void UpdateResponsiveState()
    {
        if (MacroLibraryWorkspace is null)
            return;

        var metrics = ViewModel.Settings.UiScaleMetrics;
        _isWideLayout = metrics.CanShowMacroLibraryColumns(MacroLibraryWorkspace.ActualWidth);
        if (!_isWideLayout && ViewModel.MacroLibrary.SelectedMacro is null)
            _showingDetail = false;

        MacroLibraryWorkspace.ColumnDefinitions.Clear();
        if (_isWideLayout)
        {
            MacroLibraryWorkspace.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            MacroLibraryWorkspace.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(12)
            });
            MacroLibraryWorkspace.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            Grid.SetColumn(MacroListHost, 0);
            Grid.SetColumnSpan(MacroListHost, 1);
            Grid.SetColumn(MacroDetailHost, 2);
            Grid.SetColumnSpan(MacroDetailHost, 1);
            MacroListHost.Width = metrics.MacroLibraryListWidth;
            MacroListHost.MinWidth = 240;
            MacroDetailHost.MinWidth = metrics.MacroLibraryDetailMinWidth;
            MacroListHost.Visibility = Visibility.Visible;
            MacroDetailHost.Visibility = Visibility.Visible;
            BackToMacroListButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            MacroLibraryWorkspace.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            Grid.SetColumn(MacroListHost, 0);
            Grid.SetColumnSpan(MacroListHost, 1);
            Grid.SetColumn(MacroDetailHost, 0);
            Grid.SetColumnSpan(MacroDetailHost, 1);
            MacroListHost.Width = double.NaN;
            MacroListHost.MinWidth = 0;
            MacroDetailHost.MinWidth = 0;
            MacroListHost.Visibility = _showingDetail
                ? Visibility.Collapsed
                : Visibility.Visible;
            MacroDetailHost.Visibility = _showingDetail
                ? Visibility.Visible
                : Visibility.Collapsed;
            BackToMacroListButton.Visibility = _showingDetail
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        var state = _isWideLayout
            ? "MacroLibraryWideLayout"
            : _showingDetail
                ? "MacroLibraryDetailLayout"
                : "MacroLibraryListLayout";
        if (!string.Equals(_currentLayoutState, state, StringComparison.Ordinal) &&
            VisualStateManager.GoToState(this, state, false))
        {
            _currentLayoutState = state;
        }
    }

    private void UpdateContentStates()
    {
        if (DetailEditorContent is null)
            return;

        var library = ViewModel.MacroLibrary;
        var hasSelection = library.SelectedMacro is not null;
        DetailEditorContent.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoMacroSelectedPanel.Visibility = hasSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
        MacroListEmptyState.Visibility = library.FilteredMacros.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (NoMacroReferencesTextBlock is not null)
            NoMacroReferencesTextBlock.Visibility =
            hasSelection && library.SelectedMacroReferences.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        BlockingReferencesPanel.Visibility = library.BlockingReferences.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task ImportMacroAsync()
    {
        if (ViewModel.IsRecordingSessionActive)
        {
            ViewModel.StatusText = ViewModel.IsEnglish
                ? "Stop the current macro recording first"
                : "请先停止当前宏录制";
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(
                picker,
                WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await using var stream = await file.OpenStreamForReadAsync();
            var result = await ViewModel.ImportMacroPackageAsync(stream);
            if (!result.Succeeded)
            {
                ViewModel.StatusText = ViewModel.IsEnglish
                    ? "Macro import failed"
                    : "宏导入失败";
                return;
            }

            _showingDetail = true;
            UpdateResponsiveState();
            ViewModel.StatusText = ViewModel.IsEnglish
                ? $"Imported macro draft: {result.Value.Name}"
                : $"已导入宏草稿：{result.Value.Name}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MacroLibraryPage] 宏导入失败：{ex}");
            ViewModel.StatusText = ViewModel.IsEnglish
                ? "Macro import failed"
                : "宏导入失败";
        }
    }

    private async Task ExportCurrentMacroAsync()
    {
        if (ViewModel.IsRecordingSessionActive)
            return;

        var selected = ViewModel.MacroLibrary.SelectedMacro;
        if (selected is null)
            return;

        try
        {
            var decision = DirtyMacroExportDecision.ExportSavedBaseline;
            if (selected.IsDirty)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = ViewModel.ExportDirtyMacroTitleText,
                    Content = ViewModel.ExportDirtyMacroText,
                    PrimaryButtonText = ViewModel.SaveAndExportText,
                    SecondaryButtonText = ViewModel.ExportSavedVersionText,
                    CloseButtonText = ViewModel.CancelText,
                    DefaultButton = ContentDialogButton.Close,
                    IsSecondaryButtonEnabled = !selected.IsNew
                };
                var choice = await dialog.ShowAsync();
                decision = choice switch
                {
                    ContentDialogResult.Primary => DirtyMacroExportDecision.SaveThenExport,
                    ContentDialogResult.Secondary => DirtyMacroExportDecision.ExportSavedBaseline,
                    _ => DirtyMacroExportDecision.Cancel
                };
                if (decision == DirtyMacroExportDecision.Cancel)
                    return;
            }

            var result = await PickedFileExport.ExportAsync(
                () => PickMacroDestinationAsync(selected.Name),
                stream => ReferenceEquals(selected, ViewModel.MacroLibrary.SelectedMacro)
                    ? ViewModel.ExportSelectedMacroAsync(decision, _ => Task.FromResult<Stream?>(stream))
                    : Task.FromResult(OperationResult.Failure("The selected macro changed during export.")));
            if (result is null) return;
            ViewModel.StatusText = result.Value.Succeeded
                ? (ViewModel.IsEnglish ? "Macro exported" : "宏已导出")
                : (ViewModel.IsEnglish ? "Macro export failed" : "宏导出失败");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MacroLibraryPage] 宏导出失败：{ex}");
            ViewModel.StatusText = ViewModel.IsEnglish
                ? "Macro export failed"
                : "宏导出失败";
        }
    }

    /// <summary>仅选择目标；用户取消时不保存宏、不打开写入流。</summary>
    private async Task<StorageFile?> PickMacroDestinationAsync(string macroName)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeChoices.Add(
            ViewModel.IsEnglish ? "Macro package" : "宏包",
            new[] { ".json" });
        picker.SuggestedFileName = CreateSafeFileName(macroName);

        return await picker.PickSaveFileAsync();
    }

    private async Task DeleteCurrentMacroAsync()
    {
        if (ViewModel.IsRecordingSessionActive)
            return;

        var selected = ViewModel.MacroLibrary.SelectedMacro;
        if (selected is null || selected.IsDeleted)
            return;

        var references = ViewModel.MacroLibrary.GetReferences(selected);
        if (references.Count > 0)
        {
            // 先刷新阻止列表，再让用户从详情中直接跳到引用触发器。
            ViewModel.MacroLibrary.RequestDelete(selected);
            ViewModel.StatusText = ViewModel.BlockedMacroDeletionText;
            UpdateContentStates();
            return;
        }

        ViewModel.MacroLibrary.BlockingReferences.Clear();
        if (!selected.IsNew)
        {
            DeleteMacroDialog.XamlRoot = XamlRoot;
            var result = await DeleteMacroDialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;
        }

        if (ViewModel.MacroLibrary.RequestDelete(selected))
        {
            ViewModel.StatusText = ViewModel.IsEnglish
                ? "Macro deletion is pending save"
                : "宏删除操作等待保存";
            UpdateContentStates();
        }
    }

    private static string CreateSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string((name ?? string.Empty)
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "HyperKeyX_Macro" : safe;
    }
}
