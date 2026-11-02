using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// 作者のアバター。ネットワークから取らず、名前の頭文字と、メールアドレスから決まる淡い色の角丸四角を描く
/// （Gravatar 取得は後で足す）。
/// </summary>
public sealed class Avatar : FrameworkElement
{
    public static readonly DependencyProperty NameTextProperty = DependencyProperty.Register(
        nameof(NameText), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EmailProperty = DependencyProperty.Register(
        nameof(Email), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string NameText { get => (string)GetValue(NameTextProperty); set => SetValue(NameTextProperty, value); }
    public string Email { get => (string)GetValue(EmailProperty); set => SetValue(EmailProperty, value); }

    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x96, 0xD8, 0x88), Color.FromRgb(0xFB, 0xC3, 0x74), Color.FromRgb(0x8C, 0xC8, 0xF2), Color.FromRgb(0xE8, 0x9C, 0xD0),
        Color.FromRgb(0xC6, 0xB4, 0xF0), Color.FromRgb(0xF2, 0xA0, 0x8C), Color.FromRgb(0x84, 0xD8, 0xC8), Color.FromRgb(0xE0, 0xD0, 0x80),
    ];

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[^1][0])).ToString();
    }

    public static Color ColorFor(string key)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in key.ToLowerInvariant()) h = h * 31 + c;
            return Colors[(h & 0x7FFFFFFF) % Colors.Length];
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var rect = new Rect((ActualWidth - size) / 2, (ActualHeight - size) / 2, size, size);
        var bg = new SolidColorBrush(ColorFor(string.IsNullOrEmpty(Email) ? NameText : Email));
        dc.DrawRoundedRectangle(bg, null, rect, size * 0.12, size * 0.12);
        var ft = new FormattedText(Initials(NameText), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size * 0.5, new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(rect.X + (size - ft.Width) / 2, rect.Y + (size - ft.Height) / 2));
    }
}
