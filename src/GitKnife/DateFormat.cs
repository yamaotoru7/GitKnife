using System.Globalization;
using System.Windows.Data;

namespace GitKnife;

/// <summary>
/// 画面に出す日時の書式。日付は Windows の地域設定の短い日付（ユーザーが変えた形式も含む。日本なら "2026/01/05"、
/// 米国なら "1/5/2026"）、時刻は桁を揃えるため 24 時間制のゼロ埋め（"09:05"）。
/// </summary>
public static class DateFormat
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>PC の時刻に直した日付（"2026/01/05"）。</summary>
    public static string Date(DateTimeOffset t) => Date(t.ToLocalTime().DateTime);
    public static string Date(DateTime t) => t.ToString(Culture.DateTimeFormat.ShortDatePattern, Culture);

    /// <summary>PC の時刻に直した日時（"2026/01/05 17:10"）。</summary>
    public static string DateAndTime(DateTimeOffset t) => DateAndTime(t.ToLocalTime().DateTime);
    public static string DateAndTime(DateTime t) => Date(t) + " " + t.ToString("HH:mm", Culture);

    /// <summary>秒まで（"2026/01/05 17:10:33"）。</summary>
    public static string DateAndTimeSeconds(DateTime t) => Date(t) + " " + t.ToString("HH:mm:ss", Culture);

    /// <summary>記録されたタイムゾーンのまま、秒とオフセットまで（コミット詳細の作者・コミッターの日時。"2026/01/05 17:10:33 +09:00"）。</summary>
    public static string WithOffset(DateTimeOffset t)
        => t.ToString(Culture.DateTimeFormat.ShortDatePattern, Culture) + " " + t.ToString("HH:mm:ss zzz", Culture);
}

/// <summary>
/// XAML から DateFormat を使う。ConverterParameter は "date"（日付だけ）、"offset"（タイムゾーンのまま秒とオフセットまで）、
/// 省略で日時。
/// </summary>
public sealed class DateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DateTimeOffset? t = value switch
        {
            DateTimeOffset o => o,
            System.DateTime d => new DateTimeOffset(d),
            _ => null,
        };
        if (t is not { } v) return "";
        return (parameter as string) switch
        {
            "date" => DateFormat.Date(v),
            "offset" => DateFormat.WithOffset(v),
            _ => DateFormat.DateAndTime(v),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
