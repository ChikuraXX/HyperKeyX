using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Automation.Controls;

public sealed partial class TriggerInspectorControl : UserControl
{
    public TriggerInspectorControl(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void OnClearMacroBinding(object sender, RoutedEventArgs args) =>
        ViewModel.ClearSelectedTriggerMacro();

    private void OnSaveConfig(object sender, RoutedEventArgs args) =>
        ViewModel.SaveSelectedTriggerConfig();
}
