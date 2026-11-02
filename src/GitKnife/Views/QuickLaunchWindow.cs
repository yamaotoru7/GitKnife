using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using GitKnife.Controls;
using GitKnife.Core.Text;

namespace GitKnife.Views;

/// <summary>Quick Launch の 1 項目（リポジトリかコマンド）。</summary>
/// <param name="Children">選ぶと同じ窓で次の一覧（ブランチやタグ）を出す。入力欄の左にコマンド名のチップ、
/// 空欄の案内は <see cref="ChildPlaceholder"/>（"branch"）、見出しは <see cref="ChildHeader"/>（"Branches"）、絞り込みは連続した一致。</param>
/// <param name="Icon">行のアイコン（null ならリポジトリかコンソール）。</param>
internal sealed record QuickLaunchItem(string Name, string? Path, bool IsRepository, Action Run, Func<IReadOnlyList<QuickLaunchItem>>? Children = null,
    string ChildHeader = "", string ChildPlaceholder = "", string? Icon = null);

/// <summary>
/// Quick Launch（Ctrl+P）。題名なしの 620×370 の窓をメインウィンドウの中央に出す。
/// 検索欄 PART_PlaceholderTextBox（"Command"）と RepositoriesListBox。空のときは "Recent Repositories"（8 件）と "Commands"、
/// 打つと "Commands" と "Repositories" を飛び飛びの一致で絞り、一致した文字を太字にする。Enter で実行、Esc か外を押すと閉じる。
/// </summary>
internal sealed class QuickLaunchWindow : Window
{
    private readonly IReadOnlyList<QuickLaunchItem> _recent;
    private readonly IReadOnlyList<QuickLaunchItem> _commands;
    private readonly IReadOnlyList<QuickLaunchItem> _repositories;
    /// <summary>2 段目（コマンドを選んだ後の一覧）。null なら最初の一覧。</summary>
    private (string header, IReadOnlyList<QuickLaunchItem> items)? _stage;
    private TextBlock _placeholder = null!;
    private readonly Border _chip;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private bool _closing;

    public QuickLaunchWindow(IReadOnlyList<QuickLaunchItem> recent, IReadOnlyList<QuickLaunchItem> commands, IReadOnlyList<QuickLaunchItem> repositories)
    {
        _recent = recent;
        _commands = commands;
        _repositories = repositories;
        Width = 620;
        Height = 370;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
        SetResourceReference(BackgroundProperty, "Gk.Details.Background");
        SetResourceReference(ForegroundProperty, "Gk.Text");
        SetResourceReference(FontFamilyProperty, "Gk.UiFont");
        FontSize = 13;

        var root = new DockPanel();
        var top = new DockPanel { Height = 42 };
        var icon = new IconImage { Geometry = (Geometry)FindResource("Icon.Search"), Width = 16, Height = 16, Margin = new Thickness(10, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(icon);
        // 2 段目でコマンド名を出すチップ
        var chipText = new TextBlock { FontSize = 15, Margin = new Thickness(6, 0, 6, 1) };
        _chip = new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 9, 6, 9), Child = chipText, Visibility = Visibility.Collapsed, Tag = chipText };
        _chip.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        chipText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x27));
        top.Children.Add(_chip);
        _search = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 16, VerticalContentAlignment = VerticalAlignment.Center };
        _search.SetResourceReference(TextBox.ForegroundProperty, "Gk.Text");
        _search.SetResourceReference(TextBox.CaretBrushProperty, "Gk.Text");
        AutomationProperties.SetAutomationId(_search, "PART_PlaceholderTextBox");
        _search.TextChanged += (_, _) => Fill();
        _search.PreviewKeyDown += Search_PreviewKeyDown;
        var placeholder = _placeholder = new TextBlock { Text = "Command", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), IsHitTestVisible = false };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        _search.TextChanged += (_, _) => placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchHost = new Grid();
        searchHost.Children.Add(placeholder);
        searchHost.Children.Add(_search);
        top.Children.Add(searchHost);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(line);

        _list = new ListBox { BorderThickness = new Thickness(0), Padding = new Thickness(4, 4, 4, 4), Focusable = false };
        _list.SetResourceReference(BackgroundProperty, "Gk.List.Background");
        AutomationProperties.SetAutomationId(_list, "RepositoriesListBox");
        _list.ItemContainerStyle = ItemStyle();
        _list.MouseLeftButtonUp += (_, _) => RunSelected();
        root.Children.Add(_list);

        var border = new Border { BorderThickness = new Thickness(1), Child = root };
        border.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");
        Content = border;
        Fill();
        Loaded += (_, _) => _search.Focus();
        Deactivated += (_, _) => SafeClose();
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: SafeClose(); e.Handled = true; break;
            case Key.Enter: RunSelected(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.PageDown: Move(+8); e.Handled = true; break;
            case Key.PageUp: Move(-8); e.Handled = true; break;
        }
    }

    /// <summary>見出しの行を飛ばして選択を動かす。</summary>
    private void Move(int delta)
    {
        var items = _list.Items.Cast<ListBoxItem>().ToList();
        var i = _list.SelectedIndex;
        var step = Math.Sign(delta);
        for (var n = Math.Abs(delta); n > 0; n--)
        {
            var j = i + step;
            while (j >= 0 && j < items.Count && items[j].Tag is not QuickLaunchItem) j += step;
            if (j < 0 || j >= items.Count) break;
            i = j;
        }
        if (i < 0) return;
        _list.SelectedIndex = i;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void RunSelected()
    {
        if ((_list.SelectedItem as ListBoxItem)?.Tag is not QuickLaunchItem item) return;
        if (item.Children is { } children)
        {
            _stage = (item.ChildHeader, children());
            ((TextBlock)_chip.Tag).Text = item.Name.TrimEnd('.', '…');
            _chip.Visibility = Visibility.Visible;
            _placeholder.Text = item.ChildPlaceholder;
            _search.Text = "";
            Fill();
            return;
        }
        SafeClose();
        // 窓を閉じてから実行する（ダイアログの持ち主がメインウィンドウになるように）
        Dispatcher.BeginInvoke(item.Run);
    }

    private void Fill()
    {
        _list.Items.Clear();
        var q = _search.Text.Trim();
        if (_stage is { } stage)
            Section(stage.header, q.Length == 0 ? stage.items.Select(i => (i, Array.Empty<int>())) : Contains(stage.items, q));
        else if (q.Length == 0)
        {
            Section("Recent Repositories", _recent.Select(i => (i, Array.Empty<int>())));
            Section("Commands", _commands.Select(i => (i, Array.Empty<int>())));
        }
        else
        {
            Section("Commands", Filter(_commands, q));
            Section("Repositories", Filter(_repositories, q));
        }
        Move(+1);
    }

    private static IEnumerable<(QuickLaunchItem item, int[] positions)> Filter(IReadOnlyList<QuickLaunchItem> items, string q)
        => items.Select((item, index) => (item, index, m: FuzzyMatch.Match(q, item.Name)))
            .Where(x => x.m is not null)
            .OrderByDescending(x => x.m!.Value.score).ThenBy(x => x.index)
            .Select(x => (x.item, x.m!.Value.positions));

    /// <summary>2 段目の絞り込み：連続した一致（大文字小文字は区別しない）。"fe" は feature/login だけ。</summary>
    private static IEnumerable<(QuickLaunchItem item, int[] positions)> Contains(IReadOnlyList<QuickLaunchItem> items, string q)
        => items.Select(item => (item, at: item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x.at >= 0)
            .Select(x => (x.item, Enumerable.Range(x.at, q.Length).ToArray()));

    private void Section(string header, IEnumerable<(QuickLaunchItem item, int[] positions)> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        var h = new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 6, 0, 2) };
        AutomationProperties.SetAutomationId(h, "NameTextBlock");
        var hi = new ListBoxItem { Content = h, Focusable = false, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(hi, "HeaderCommandProviderItem");
        _list.Items.Add(hi);
        foreach (var (item, positions) in list) _list.Items.Add(Row(item, positions));
    }

    private static ListBoxItem Row(QuickLaunchItem item, int[] positions)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        var icon = new IconImage { Geometry = (Geometry)Application.Current.FindResource(item.Icon ?? (item.IsRepository ? "Icon.Repository" : "Icon.Console")), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(icon, "IconImage");
        panel.Children.Add(icon);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
        AutomationProperties.SetAutomationId(name, "NameTextBlock");
        AutomationProperties.SetName(name, item.Name);
        // 一致した文字を太字に
        var set = positions.ToHashSet();
        for (var i = 0; i < item.Name.Length;)
        {
            var bold = set.Contains(i);
            var j = i;
            while (j < item.Name.Length && set.Contains(j) == bold) j++;
            var run = new Run(item.Name[i..j]);
            if (bold) run.FontWeight = FontWeights.Bold;
            name.Inlines.Add(run);
            i = j;
        }
        panel.Children.Add(name);
        if (item.Path is not null)
        {
            var dash = new TextBlock { Text = " — ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(dash, "DashTextBlock");
            var path = new TextBlock { Text = item.Path, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(path, "PathTextBlock");
            dash.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
            path.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
            panel.Children.Add(dash);
            panel.Children.Add(path);
        }
        return new ListBoxItem { Content = panel, Tag = item };
    }

    private static Style ItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Gk.Text")));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = bd };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.List.Hover"), "Bd"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.Selection"), "Bd"));
        selected.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }
}
