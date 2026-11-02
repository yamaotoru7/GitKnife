using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.ViewModels;

namespace GitKnife;

/// <summary>正の数なら Visible。</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>true なら Visible（parameter に "invert" で逆、"hidden" で Collapsed の代わりに Hidden）。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        var p = parameter as string ?? "";
        if (p.Contains("invert")) b = !b;
        // "hidden"：場所を残して隠す（UIA のツリーにも offscreen の要素として残る）
        return b ? Visibility.Visible : p.Contains("hidden") ? Visibility.Hidden : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null や空文字なら Collapsed。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>enum の値と parameter（名前）が等しいか。RadioButton の IsChecked に使う。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Enum.Parse(targetType, (string)parameter) : Binding.DoNothing;
}

/// <summary>enum が引数と同じなら Visible、違えば Collapsed（サイドバーの Branches / Search の切り替え）。</summary>
public sealed class EnumToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter as string ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Thickness を 2 倍にする。Gk.TextBox は Padding が入力の文字に 2 回効くので、空のときの案内文（PlaceholderText）も同じ位置にそろえる
/// （2026-10-27：サイドバーの検索欄で案内文だけ左にずれて虫眼鏡に重なっていた）。
/// </summary>
public sealed class DoubleThicknessConverter : IValueConverter
{
    public static readonly DoubleThicknessConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Thickness t ? new Thickness(t.Left * 2, t.Top, t.Right * 2, t.Bottom) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>サイドバーの階層 → 左の余白。</summary>
public sealed class LevelToIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(4 + (value is int l ? l : 0) * 10, 0, 0, 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>グラフの列数 → グラフ列の幅。</summary>
public sealed class GraphWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => GraphCell.LeftPadding + Math.Max(1, value is int w ? w : 1) * GraphCell.LaneWidth;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>コミット一覧のラベル（CommitRow と RefLabel の組）→ 背景・枠・アイコン。</summary>
public sealed class RefLabelBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not RefLabel label || values[1] is not CommitRow row) return Brushes.Transparent;
        var part = parameter as string;
        var listBg = (Application.Current.TryFindResource("Gk.List.Background") as SolidColorBrush)?.Color ?? Colors.Black;
        switch (label.Kind)
        {
            case RefLabelKind.Tag:
                return part == "border" ? Application.Current.TryFindResource("Gk.Label.TagBorder")! : Application.Current.TryFindResource("Gk.Label.Tag")!;
            case RefLabelKind.Stash:
                return part == "border" ? Application.Current.TryFindResource("Gk.Label.StashBorder")! : Application.Current.TryFindResource("Gk.Label.Stash")!;
            default:
                // Light は淡い地に列の色の枠（文字は Gk.Label.Text の濃い色）
                if (!ThemeManager.IsDark)
                    return part == "border" ? GraphPalette.LightLabelBorder(row.Graph.NodeColor) : GraphPalette.LightLabelBackground(row.Graph.NodeColor);
                return part == "border" ? GraphPalette.Line(row.Graph.NodeColor) : GraphPalette.LabelBackground(row.Graph.NodeColor, listBg);
        }
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ラベル → アイコン（現在のブランチはチェック、upstream と同期しているブランチとリモートブランチは雲、タグ、stash）。</summary>
public sealed class RefLabelIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not RefLabel l ? null : l.Kind switch
    {
        RefLabelKind.CurrentBranch => Application.Current.TryFindResource("Icon.Check"),
        // リモートの雲はラベルの中ではなく、テンプレートの暗い区画（RemotePart）と前の小さな枠（SyncChip）に描く
        RefLabelKind.Tag => Application.Current.TryFindResource("Icon.Tag"),
        RefLabelKind.Stash => Application.Current.TryFindResource("Icon.Stash"),
        _ => null,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>サイドバー項目の種類 → アイコン。</summary>
public sealed class SidebarIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SidebarItem item) return null;
        var key = item.Kind switch
        {
            SidebarItemKind.LocalBranch when item.IsCurrentBranch => "Icon.Check",
            SidebarItemKind.LocalBranch or SidebarItemKind.RemoteBranch => "Icon.Branch",
            SidebarItemKind.Folder => "Icon.Folder",
            SidebarItemKind.Remote => "Icon.Remote",
            SidebarItemKind.Tag => "Icon.Tag",
            SidebarItemKind.Stash => "Icon.Stash",
            SidebarItemKind.Worktree => "Icon.Worktree",
            SidebarItemKind.Submodule => "Icon.Link",
            _ => null,
        };
        return key is null ? null : Application.Current.TryFindResource(key);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null や空文字なら Visible（透かし文字用）。</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null || value is string { Length: 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>true ならリソース（parameter のキー）のブラシ、false なら透明（2 コミット比較の SHA の地の色）。</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && Application.Current.TryFindResource(parameter as string ?? "") is System.Windows.Media.Brush b ? b : System.Windows.Media.Brushes.Transparent;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>enum の値が parameter（名前）と等しければ Visible（DetailsMode ごとのパネル）。"!名前" なら等しくないとき Visible。</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public static readonly EnumToVisibilityConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // "!Commit" のように先頭が ! なら逆
        var p = parameter as string ?? "";
        var equal = value?.ToString() == p.TrimStart('!');
        return equal != p.StartsWith('!') ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ファイル一覧（Tree 表示）の階層 → 左の余白。</summary>
public sealed class FileIndentConverter : IValueConverter
{
    public static readonly FileIndentConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(2 + (value is int l ? l : 0) * 16, 0, 0, 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>bool を反転する（Fetch all のとき Remote を無効にするなど）。</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>タブの色の名前 → ブラシ（色。名前が空なら灰色の点）。</summary>
public sealed class TabColorBrushConverter : IValueConverter
{
    public static readonly TabColorBrushConverter Instance = new();

    public static readonly IReadOnlyDictionary<string, Brush> Brushes = new Dictionary<string, Brush>
    {
        ["Red"] = Frozen(0xFF, 0x3B, 0x30),
        ["Orange"] = Frozen(0xFF, 0x95, 0x02),
        ["Yellow"] = Frozen(0xFF, 0xCC, 0x00),
        ["Green"] = Frozen(0x64, 0xDA, 0x38),
        ["Blue"] = Frozen(0x1C, 0xAD, 0xF8),
        ["Violet"] = Frozen(0xCB, 0x73, 0xE1),
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && Brushes.TryGetValue(s, out var b) ? b : Application.Current.TryFindResource("Gk.Text.Dim");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>push / pull の点の色（青 #3E9FF8、灰色 #A9A9A9）。</summary>
public sealed class SyncMarkBrushConverter : IValueConverter
{
    public static readonly SyncMarkBrushConverter Instance = new();
    private static readonly Brush PushBrush = Frozen(Color.FromRgb(0x3E, 0x9F, 0xF8));
    private static readonly Brush PullBrush = Frozen(Color.FromRgb(0xA9, 0xA9, 0xA9));

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SyncMark.Push ? PushBrush : value is SyncMark.Pull ? PullBrush : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
