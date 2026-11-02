using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using GitKnife.Controls;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// ダイアログの本文（右寄せのラベル列 + 入力列）をコードで組み立てる。Fetch / Pull / Push 以外の小さなダイアログはこれで作る。
/// 束縛のパスは DialogViewModel のプロパティ名。
/// </summary>
internal static class FormExtensions
{
    /// <summary>コードで作った部品に束縛を付ける（Form の行に入れる前に）。</summary>
    public static T Bind<T>(this T element, DependencyProperty property, string path) where T : FrameworkElement
    {
        element.SetBinding(property, new Binding(path));   // 向きはプロパティの既定（SelectedItem は TwoWay）
        return element;
    }

    /// <summary>開いたときに入力の焦点を当てない（Finish Git Flow release / hotfix の Message など）。</summary>
    public static TextBox WithoutAutoFocus(this TextBox t)
    {
        t.Tag = "no-autofocus";
        return t;
    }
}

internal sealed class Form : Grid
{
    /// <summary>
    /// ダイアログの本文：題名の位置から始め（アイコンの列 64 + 間 16）、ラベルの列は一番長いラベルに合わせる
    /// （ラベルの左端が題名の左端と同じ）。
    /// </summary>
    public Form() : this(null) => Margin = new Thickness(80, 0, 0, 0);

    /// <summary>ラベルの列の幅を決めた形（設定画面のカードなど、見出しのアイコンが無い所）。null なら中身に合わせる。</summary>
    public Form(double? labelWidth)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = labelWidth is { } w ? new GridLength(w) : GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    /// <summary>1 行足す。label が null なら入力列だけ。visibleWhen を渡すとその bool が true のときだけ出す。labelId はラベルの AutomationId。</summary>
    public Form Row(string? label, FrameworkElement element, double height = 32, string? visibleWhen = null, string? labelId = null)
    {
        // 値だけの行（Value）は 26px おき。文字は 32px の行の中ほどと同じ高さに置く（Reset の Branch: と Move to: など。
        // 下の入力欄やラジオとの間も狭い）
        var valueRow = element.Tag is ValueTag && height == 32 && visibleWhen is null;
        if (valueRow)
        {
            height = 26;
            element.VerticalAlignment = VerticalAlignment.Top;
            element.Margin = new Thickness(element.Margin.Left, 7.5, 0, 0);
        }
        RowDefinitions.Add(new RowDefinition { Height = visibleWhen is null ? new GridLength(height) : GridLength.Auto });
        var row = RowDefinitions.Count - 1;
        if (visibleWhen is not null) element.MinHeight = height;
        else if (height > 40) element.Margin = new Thickness(0, 4, 0, 4);
        if (label is not null)
        {
            // 高い行（縦に並んだラジオ、複数行の入力）はラベルを上にそろえる
            var t = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = height > 40 || valueRow ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, height > 40 ? 6 : valueRow ? 7.5 : 0, 8, 0) };
            if (labelId is not null) AutomationProperties.SetAutomationId(t, labelId);
            SetRow(t, row);
            if (visibleWhen is not null) BindVisible(t, visibleWhen);
            Children.Add(t);
        }
        if (!valueRow) element.VerticalAlignment = VerticalAlignment.Center;
        SetRow(element, row);
        SetColumn(element, 1);
        if (visibleWhen is not null) BindVisible(element, visibleWhen);
        Children.Add(element);
        return this;
    }

    /// <summary>Value で作った部品の印（Tag）。</summary>
    private sealed class ValueTag { public static readonly ValueTag Instance = new(); }

    private static void BindVisible(FrameworkElement e, string path)
        => e.SetBinding(VisibilityProperty, new Binding(path) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });

    // ------------------------------------------------------------------ 部品

    public static TextBox Text(string id, string path)
    {
        var t = new TextBox { Height = 24 };
        t.SetResourceReference(StyleProperty, "Gk.TextBox");
        // 上下の余白は付けない。日本語の Windows の UI フォント（Yu Gothic UI）は行が高く、24 の中に余白 2+2 を取ると文字の下が切れる（#7）
        t.Padding = new Thickness(4, 0, 4, 0);
        AutomationProperties.SetAutomationId(t, id);
        t.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        t.Loaded += (_, _) => { if (FocusFirst(t)) t.SelectAll(); };
        return t;
    }

    /// <summary>複数行の入力（タグのメッセージなど）。</summary>
    public static TextBox MultiLine(string id, string path, double height = 72)
    {
        var t = Text(id, path);
        t.Height = height;
        t.AcceptsReturn = true;
        t.TextWrapping = TextWrapping.Wrap;
        t.VerticalContentAlignment = VerticalAlignment.Top;
        t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        return t;
    }

    public static CheckBox Check(string id, string content, string path)
    {
        var c = new CheckBox { Content = content };
        AutomationProperties.SetAutomationId(c, id);
        c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path));
        return c;
    }

    public static RadioButton Radio(string id, string content, string group, string path, object value)
    {
        var r = new RadioButton { Content = content, GroupName = group, Foreground = (Brush)Application.Current.FindResource("Gk.Text"), Margin = new Thickness(0, 0, 14, 0) };
        AutomationProperties.SetAutomationId(r, id);
        r.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(path) { Converter = (IValueConverter)Application.Current.FindResource("Gk.EnumEquals"), ConverterParameter = value.ToString() });
        return r;
    }

    /// <summary>アイコンつきの値の表示（"Create branch at:  ⑂ main" など）。</summary>
    public static StackPanel Value(Geometry? icon, string path, string? id = null)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), Tag = ValueTag.Instance };
        if (icon is not null)
        {
            var path2 = new Path { Data = icon, Margin = new Thickness(0, 0, 6, 0) };
            path2.SetResourceReference(StyleProperty, "Gk.IconPath");
            p.Children.Add(path2);
        }
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (id is not null) AutomationProperties.SetAutomationId(t, id);
        t.SetBinding(TextBlock.TextProperty, new Binding(path));
        p.Children.Add(t);
        return p;
    }

    /// <summary>淡い注記（"By default stash ignores new files until you stage them" など）。</summary>
    public static TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        return t;
    }

    public static ComboBox Combo(string id, string itemsPath, string selectedPath)
    {
        var c = new ComboBox();
        AutomationProperties.SetAutomationId(c, id);
        c.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
        c.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(selectedPath));
        return c;
    }

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static Geometry Icon(string key) => (Geometry)Application.Current.FindResource(key);

    // 最初の入力欄にフォーカス（ダイアログを開いたらすぐ打てるように）
    private static bool FocusFirst(TextBox t)
    {
        if (t.Tag is "no-autofocus" || Window.GetWindow(t) is not { } w || w.Tag is "focused") return false;
        w.Tag = "focused";
        t.Focus();
        return true;
    }
}
