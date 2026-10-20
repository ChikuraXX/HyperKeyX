using HyperKeyX.Features.Mouse.ViewModels;
using HyperKeyX.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Mouse.Controls;

/// <summary>
/// 提供五个可配置鼠标按钮的统一选择入口。
/// </summary>
public sealed partial class MouseButtonSelectorControl : UserControl
{
    public MouseButtonSelectorControl(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void OnMouseButtonClick(object sender, RoutedEventArgs args)
    {
        // 编译绑定模板的项目显式随 Tag 传递，不依赖外层继承的 DataContext。
        if (sender is FrameworkElement { Tag: MouseButtonStateViewModel state })
            ViewModel.SelectMouseButtonCommand.Execute(state);
    }
}
