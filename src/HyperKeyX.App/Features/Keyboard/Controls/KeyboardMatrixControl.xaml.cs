using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Keyboard.Controls;

public sealed partial class KeyboardMatrixControl : UserControl
{
    public MainViewModel ViewModel { get; }

    public KeyboardMatrixControl(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
