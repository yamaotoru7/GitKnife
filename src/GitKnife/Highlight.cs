using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GitKnife;

/// <summary>
/// TextBlock の文字のうち Term に一致する所（大文字小文字を区別しない）に色を付ける添付プロパティ（Search タブの結果。一致した所を黄土色の地にする）。
/// </summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata("", (d, _) => Update(d)));
    public static readonly DependencyProperty TermProperty = DependencyProperty.RegisterAttached(
        "Term", typeof(string), typeof(Highlight), new PropertyMetadata("", (d, _) => Update(d)));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetTerm(DependencyObject d) => (string)d.GetValue(TermProperty);
    public static void SetTerm(DependencyObject d, string value) => d.SetValue(TermProperty, value);

    /// <summary>一致した所の地の色（省略すると Search タブの色。コミット一覧の Ctrl+F は #B07600）。</summary>
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(Highlight), new PropertyMetadata(null, (d, _) => Update(d)));
    public static Brush? GetBrush(DependencyObject d) => (Brush?)d.GetValue(BrushProperty);
    public static void SetBrush(DependencyObject d, Brush? value) => d.SetValue(BrushProperty, value);

    /// <summary>
    /// 件名の先頭の、空白を含まずに ":" まで続く部分を太字にする（"feat:"、"fix(ui):"、"アルバムをすぐ開く:"。"Claude API の費用を下げる:" のように空白があれば太字にしない）。
    /// コミット一覧の件名。
    /// </summary>
    public static readonly DependencyProperty BoldPrefixProperty = DependencyProperty.RegisterAttached(
        "BoldPrefix", typeof(bool), typeof(Highlight), new PropertyMetadata(false, (d, _) => Update(d)));
    public static bool GetBoldPrefix(DependencyObject d) => (bool)d.GetValue(BoldPrefixProperty);
    public static void SetBoldPrefix(DependencyObject d, bool value) => d.SetValue(BoldPrefixProperty, value);

    private static readonly System.Text.RegularExpressions.Regex Prefix = new(@"^[^\s:]+:(?=\s|$)");

    private static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x9A, 0x6A, 0x10));

    private static void Update(DependencyObject d)
    {
        if (d is not TextBlock tb) return;
        var text = GetText(tb) ?? "";
        var term = GetTerm(tb) ?? "";
        tb.Inlines.Clear();
        var bold = GetBoldPrefix(tb) && Prefix.Match(text) is { Success: true } m ? m.Length : 0;
        // 太字の境目で Run を分ける
        void Add(int start, int end, Brush? back)
        {
            if (start < bold && end > bold) { Add(start, bold, back); Add(bold, end, back); return; }
            if (end <= start) return;
            var run = new Run(text[start..end]);
            if (back is not null) run.Background = back;
            if (end <= bold) run.FontWeight = FontWeights.Bold;
            tb.Inlines.Add(run);
        }
        if (term.Length == 0) { Add(0, text.Length, null); return; }
        var pos = 0;
        while (pos < text.Length)
        {
            var i = text.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
            if (i < 0) { Add(pos, text.Length, null); break; }
            Add(pos, i, null);
            Add(i, i + term.Length, GetBrush(tb) ?? Back);
            pos = i + term.Length;
        }
    }
}
