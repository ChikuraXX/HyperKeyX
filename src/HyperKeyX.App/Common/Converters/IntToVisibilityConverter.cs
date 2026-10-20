using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace HyperKeyX.Common.Converters;

/// <summary>
/// 将整数零转换为可见状态，供空宏步骤提示使用。
/// </summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is int count && count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
