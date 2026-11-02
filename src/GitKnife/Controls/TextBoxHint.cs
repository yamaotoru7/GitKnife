using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// Gk.TextBox の左のアイコンと、空のときの薄い説明文（"Enter branch name" など）。
/// どちらも付けた TextBox の見た目だけを変える（Text には入らない）。
/// </summary>
public static class TextBoxHint
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(TextBoxHint), new PropertyMetadata(null));
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(TextBoxHint), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject d) => (string?)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string? value) => d.SetValue(PlaceholderProperty, value);
    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, Geometry? value) => d.SetValue(IconProperty, value);

    /// <summary>コードで作った TextBox に付ける。iconKey は Icons.xaml のキー。</summary>
    public static TextBox Hint(this TextBox t, string? placeholder, string? iconKey = null)
    {
        SetPlaceholder(t, placeholder);
        if (iconKey is not null) SetIcon(t, (Geometry)Application.Current.FindResource(iconKey));
        return t;
    }
}
