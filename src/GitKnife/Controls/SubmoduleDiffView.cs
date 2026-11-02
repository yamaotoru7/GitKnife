using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.ViewModels;

namespace GitKnife.Controls;

/// <summary>
/// submodule の diff。上に "Submodule 'sublib' changed 1↑"（戻ったときは ↓）、
/// 古いコミットと新しいコミットの行（アバター、作者、sha（古い方は赤、新しい方は緑）、日時、件名）、
/// その下に 2 つのコミットの間のコミット（左にラベルと件名、右に作者・sha・日時）。
/// diff は `diff --submodule=short` の "Subproject commit" の行（SubmoduleChange）、コミットは submodule の中で `log` で読む。
/// 間のコミットの左は点だけ（グラフの線は描かない）。
/// </summary>
internal sealed class SubmoduleDiffView : UserControl
{
    private const string Format = "--format=%H%x1f%an%x1f%ae%x1f%at%x1f%s%x1f%D";
    private readonly StackPanel _root = new() { Margin = new Thickness(10, 8, 10, 8) };
    private int _loadVersion;

    public SubmoduleDiffView()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static bool IsSubmodule(FileDiff? diff) => diff is not null && SubmoduleChange.TryParse(diff) is not null;

    public async Task LoadAsync(RepositoryViewModel repo, FileDiff diff)
    {
        var version = ++_loadVersion;
        _root.Children.Clear();
        if (SubmoduleChange.TryParse(diff) is not { } change) return;
        var path = $"{repo.Path}/{diff.Path}";
        async Task<List<string[]>> Log(params string[] args)
        {
            var r = await repo.Git.RunAsync(new GitRequest(path, ["log", Format, .. args]));
            return r.Succeeded ? r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Split('\x1f')).Where(f => f.Length >= 6).ToList() : [];
        }
        var ends = await Log(["--no-walk=unsorted", .. new[] { change.Old, change.New }.OfType<string>()]);
        var between = change is { Old: { } o, New: { } n } && o != n ? await Log([$"{o}...{n}", "--left-right", "--boundary"]) : [];
        int ahead = 0, behind = 0;
        if (change is { Old: { } o2, New: { } n2 } && o2 != n2)
        {
            var count = await repo.Git.RunAsync(new GitRequest(path, ["rev-list", "--left-right", "--count", $"{o2}...{n2}"]));
            var parts = count.StandardOutput.Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) { int.TryParse(parts[0], out behind); int.TryParse(parts[1], out ahead); }
        }
        if (version != _loadVersion) return;

        var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        title.Inlines.Add(new System.Windows.Documents.Run($"Submodule '{diff.Path}' {(change.Old is null ? "added" : change.New is null ? "removed" : "changed")}"));
        var counts = (ahead > 0 ? $" {ahead}↑" : "") + (behind > 0 ? $" {behind}↓" : "") + (change.Dirty ? " (dirty)" : "");
        if (counts.Length > 0) title.Inlines.Add(new System.Windows.Documents.Run(counts) { FontSize = 12, FontWeight = FontWeights.Normal });
        AutomationProperties.SetAutomationId(title, "SubmoduleTitleTextBlock");
        _root.Children.Add(title);

        foreach (var (sha, color) in new[] { (change.Old, Color.FromRgb(0x6E, 0x2B, 0x2B)), (change.New, Color.FromRgb(0x2B, 0x6E, 0x3C)) })
        {
            if (sha is null || ends.FirstOrDefault(f => f[0] == sha) is not { } c) continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Avatar { NameText = c[1], Email = c[2], Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(Text(c[1], bold: true));
            row.Children.Add(Badge(sha[..9], new SolidColorBrush(color)));
            row.Children.Add(Text(Date(c[3]), size: 11.5));
            row.Children.Add(Text(c[4], bold: true, margin: 6));
            _root.Children.Add(row);
        }

        if (between.Count == 0) return;
        var line = new Border { Height = 1, Margin = new Thickness(-10, 6, -10, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        _root.Children.Add(line);
        var list = new Grid();
        list.ColumnDefinitions.Add(new ColumnDefinition());
        list.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var listBg = (TryFindResource("Gk.List.Background") as SolidColorBrush)?.Color ?? Colors.Black;
        for (var i = 0; i < between.Count; i++)
        {
            var f = between[i];
            list.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = GraphPalette.Line(0), Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(dot);
            foreach (var label in f[5].Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(l => l.Replace("HEAD -> ", "")).Where(l => l != "HEAD" && !l.EndsWith("/HEAD", StringComparison.Ordinal)))
                left.Children.Add(Badge(label, GraphPalette.LabelBackground(0, listBg), GraphPalette.Line(0)));
            left.Children.Add(Text(f[4], margin: 2));
            Grid.SetRow(left, i);
            list.Children.Add(left);
            // 右は 作者（幅 140）・sha・日時の列にそろえる
            var right = new Grid();
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            var who = new StackPanel { Orientation = Orientation.Horizontal };
            who.Children.Add(new Avatar { NameText = f[1], Email = f[2], Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            who.Children.Add(Text(f[1], margin: 0));
            right.Children.Add(who);
            var sha7 = Badge(f[0][..7], new SolidColorBrush(f[0] == change.New ? Color.FromRgb(0x2B, 0x6E, 0x3C) : Color.FromRgb(0x6E, 0x2B, 0x2B)));
            Grid.SetColumn(sha7, 1);
            right.Children.Add(sha7);
            var when = Text(Date(f[3]), size: 11.5);
            Grid.SetColumn(when, 2);
            right.Children.Add(when);
            Grid.SetRow(right, i);
            Grid.SetColumn(right, 1);
            list.Children.Add(right);
        }
        _root.Children.Add(list);
    }

    private static string Date(string unix)
        => long.TryParse(unix, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("d M yyyy HH:mm") : "";

    private static TextBlock Text(string text, bool bold = false, double size = 13, double margin = 6)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, margin, 0) };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text");
        return t;
    }

    private static Border Badge(string text, Brush background, Brush? border = null)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(4, 0, 4, 1) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text");
        return new Border
        {
            Child = t, Background = background, BorderBrush = border ?? background, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
