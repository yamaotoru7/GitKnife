using System.IO;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace GitKnife.Controls;

/// <summary>
/// diff の構文ハイライト。AvalonEdit の標準の定義を拡張子で選び、Dark テーマでは色を置き換える
/// （キーワード #649BD1、修飾子 #E64C80、文字列 #CF9F89、コメント #6A9955、数値 #B5CEA8、そのほか #DDDDDD）。
/// Preferences の "Disable syntax highlighting"（DisableSyntaxHighlighting）なら使わない。
/// </summary>
internal static class SyntaxColors
{
    private static readonly Dictionary<string, IHighlightingDefinition?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Color Keyword = Color.FromRgb(0x64, 0x9B, 0xD1);
    private static readonly Color Modifier = Color.FromRgb(0xE6, 0x4C, 0x80);
    private static readonly Color Text = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly Color StringColor = Color.FromRgb(0xCF, 0x9F, 0x89);
    private static readonly Color Comment = Color.FromRgb(0x6A, 0x99, 0x55);
    private static readonly Color Number = Color.FromRgb(0xB5, 0xCE, 0xA8);

    public static IHighlightingDefinition? For(string? path)
    {
        if (App.Settings.DisableSyntaxHighlighting || string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path);
        if (ext.Length == 0) return null;
        if (Cache.TryGetValue(ext, out var cached)) return cached;
        var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        // 標準の定義の色は明るい背景向けなので、Dark のときだけ暗い背景向けの色に置き換える（定義はアプリ全体で 1 つなので一度だけ。途中でテーマを変えると古い色のまま）
        if (def is not null) KeepFont(def);
        if (def is not null && ThemeManager.IsDark) Recolor(def);
        return Cache[ext] = def;
    }

    /// <summary>標準の定義は Markdown の見出しなどで文字の大きさ・書体まで変える。diff の行の高さが崩れるので、色と太さだけを使う。</summary>
    private static void KeepFont(IHighlightingDefinition def)
    {
        foreach (var c in def.NamedHighlightingColors)
        {
            c.FontSize = null;
            c.FontFamily = null;
        }
    }

    private static void Recolor(IHighlightingDefinition def)
    {
        foreach (var c in def.NamedHighlightingColors)
        {
            var name = c.Name.ToLowerInvariant();
            Color? color =
                name.Contains("comment") ? Comment :
                name.Contains("string") || name.Contains("char") ? StringColor :
                name.Contains("number") || name.Contains("digit") ? Number :
                name.Contains("modifier") || name.Contains("visibility") || name.Contains("goto") ? Modifier :
                name.Contains("keyword") || name.Contains("heading") || name.Contains("type") || name.Contains("truefalse") || name.Contains("null") || name.Contains("thisorbase") ? Keyword :
                name.Contains("method") || name.Contains("punctuation") ? Text :
                null;
            if (color is null) continue;
            c.Foreground = new SimpleHighlightingBrush(color.Value);
            c.FontWeight = FontWeights.Normal;
            c.FontStyle = FontStyles.Normal;
        }
    }
}
