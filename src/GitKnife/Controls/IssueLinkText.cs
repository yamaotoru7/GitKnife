using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GitKnife.Core.Settings;

namespace GitKnife.Controls;

/// <summary>
/// コミットのメッセージの中の課題番号をリンクにする（Repository Settings > Issue Tracker のルール）。
/// TextBlock に Text と Rules を付けると、ルールに当たった所を Hyperlink にして書き直す。ルールが無ければ普通の文字。
/// </summary>
public static class IssueLinkText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(IssueLinkText), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty RulesProperty = DependencyProperty.RegisterAttached("Rules", typeof(IReadOnlyList<IssueTrackerRule>), typeof(IssueLinkText), new PropertyMetadata(null, Changed));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
    public static IReadOnlyList<IssueTrackerRule>? GetRules(DependencyObject d) => (IReadOnlyList<IssueTrackerRule>?)d.GetValue(RulesProperty);
    public static void SetRules(DependencyObject d, IReadOnlyList<IssueTrackerRule>? value) => d.SetValue(RulesProperty, value);

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var text = GetText(tb) ?? "";
        tb.Inlines.Clear();
        var links = GetRules(tb) is { Count: > 0 } rules ? IssueTracker.FindLinks(text, rules) : [];
        var at = 0;
        foreach (var l in links)
        {
            if (l.Start > at) tb.Inlines.Add(new Run(text[at..l.Start]));
            var link = new Hyperlink(new Run(text.Substring(l.Start, l.Length))) { ToolTip = l.Url };
            link.SetResourceReference(TextElement.ForegroundProperty, "Gk.Accent");
            var url = l.Url;
            link.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
            };
            tb.Inlines.Add(link);
            at = l.Start + l.Length;
        }
        if (at < text.Length) tb.Inlines.Add(new Run(text[at..]));
    }
}
