using System.Globalization;
using System.Windows.Data;

namespace GitKnife.ViewModels;

/// <summary>40 桁の SHA → 7 桁（PARENTS のリンク。"a03091b"）。</summary>
public sealed class ShortShaConverter : IValueConverter
{
    public static readonly ShortShaConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string s && s.Length > 7 ? s[..7] : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
