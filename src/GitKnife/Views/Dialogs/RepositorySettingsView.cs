using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.ViewModels.Dialogs;
using static GitKnife.Views.Dialogs.Form;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Repository Settings の本文。
/// 窓の幅はタブごとに違う（General 496、Commit 454、Issue Tracker 646）。
/// </summary>
internal sealed class RepositorySettingsView : UserControl
{
    private static readonly double[] TabWidths = [454, 411, 604, 904];
    private readonly RepositorySettingsViewModel _vm;

    public RepositorySettingsView(RepositorySettingsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        var tabs = new TabControl { Width = TabWidths[0], Margin = new Thickness(0, 6, 0, 0) };
        tabs.SetResourceReference(StyleProperty, "Gk.TabControl");
        tabs.Items.Add(Tab("General", "GeneralUserControl", General()));
        tabs.Items.Add(Tab("Commit", "CommitTemplateUserControl", Commit()));
        tabs.Items.Add(Tab("Issue Tracker", "IssueTrackerUserControl", IssueTracker()));
        var custom = Tab("Custom Commands", "CustomCommandsUserControl", CustomCommands());
        AutomationProperties.SetAutomationId(custom, "CustomCommandsTab");
        tabs.Items.Add(custom);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || Window.GetWindow(this) is not { } w) return;
            // SizeToContent が Height のままだと Width を入れても戻されるので、いったん Manual にする（PreferencesView と同じ）
            tabs.Width = TabWidths[Math.Max(0, tabs.SelectedIndex)];
            w.SizeToContent = SizeToContent.Manual;
            w.Width = tabs.Width + 42;
            w.SizeToContent = SizeToContent.Height;
        };
        Content = tabs;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    private static TabItem Tab(string header, string id, FrameworkElement body)
    {
        var t = new TabItem { Header = header };
        t.SetResourceReference(StyleProperty, "Gk.TabItem");
        AutomationProperties.SetAutomationId(body, id);
        body.SetValue(TextElement.FontWeightProperty, FontWeights.Normal);
        t.Content = body;
        return t;
    }

    private FrameworkElement General()
    {
        var p = new SettingsPage(narrowHeaders: true, indent: 112);
        p.Header("User Information");
        p.Check("UseGlobalGitCredentialsCheckBox", "Use global git credentials", nameof(RepositorySettingsViewModel.UseGlobalCredentials));

        var avatar = new Avatar { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 10, 0), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(avatar, "AuthorAvatarImage");
        avatar.SetBinding(Avatar.NameTextProperty, new Binding(nameof(RepositorySettingsViewModel.UserName)));
        avatar.SetBinding(Avatar.EmailProperty, new Binding(nameof(RepositorySettingsViewModel.Email)));
        var name = Text("UserNameTextBox", nameof(RepositorySettingsViewModel.UserName));
        var email = Text("EmailTextBox", nameof(RepositorySettingsViewModel.Email));
        email.Margin = new Thickness(0, 8, 0, 0);
        foreach (var box in new[] { name, email })
            box.SetBinding(IsEnabledProperty, new Binding(nameof(RepositorySettingsViewModel.UseGlobalCredentials)) { Converter = Not.Instance });
        var fields = new StackPanel { Children = { name, email, Gravatar() } };
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(avatar);
        Grid.SetColumn(fields, 1);
        grid.Children.Add(fields);
        // チェックボックスと名前の欄の間を空ける（名前の欄の上端がチェックボックスの文字の中ほどから 24px 下）
        p.Content(grid, top: 14, indent: 0);
        p.Separator();

        p.Header("Lean Branching");
        var branch = new Button { Height = 24, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        branch.SetResourceReference(StyleProperty, "Gk.PickerButton");
        AutomationProperties.SetAutomationId(branch, "toggleButton");
        // ▾ は右端の列に（DockPanel だとボタンの右の外に置かれて見えなかった。RefPicker と同じ並べ方）
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = new IconImage { Geometry = Icon("Icon.ChevronDown"), Width = 12, Height = 12, Margin = new Thickness(6, 0, 4, 0) };
        Grid.SetColumn(chevron, 1);
        content.Children.Add(Value(Icon("Icon.Branch"), nameof(RepositorySettingsViewModel.MainBranchText)));
        content.Children.Add(chevron);
        branch.Content = content;
        // 押すと "default (main)" とローカルブランチ
        branch.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = branch, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = branch.ActualWidth };
            void Item(string header, string? value)
            {
                var mi = new MenuItem { Header = header, FontWeight = _vm.MainBranch == value ? FontWeights.SemiBold : FontWeights.Normal };
                mi.Click += (_, _) => _vm.MainBranch = value;
                menu.Items.Add(mi);
            }
            Item(_vm.DefaultMainBranchText, null);
            menu.Items.Add(new Separator());
            foreach (var b in _vm.LocalBranches) Item(b, b);
            menu.IsOpen = true;
        };
        p.Row("Main Branch:", branch);
        p.Check("NoFastForwardCheckBox", "No fast-forward on merge", nameof(RepositorySettingsViewModel.NoFastForward));
        p.Separator(top: 20);   // チェックボックスから線まで 29px

        p.Header("Diff View");
        var tab = PreferencesView.Number("TabWidthTextBox", nameof(RepositorySettingsViewModel.TabWidth));
        tab.HorizontalAlignment = HorizontalAlignment.Left;
        p.Row("Tab Width:", tab);
        return p;
    }

    private FrameworkElement Commit()
    {
        var p = new SettingsPage(narrowHeaders: true, indent: 0);
        p.Header("Commit Template");
        p.Content(Form.Check("UseGlobalCommitTemplateCheckBox", "Use global git configuration file", nameof(RepositorySettingsViewModel.UseGlobalTemplate)), top: 4, indent: 0);
        var path = new TextBlock { Margin = new Thickness(42, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(path, "CommitTemplatePathTextBlock");
        path.SetBinding(TextBlock.TextProperty, new Binding(nameof(RepositorySettingsViewModel.TemplatePath)));
        path.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        p.Children.Add(path);
        var text = MultiLine("CommitTemplateTextBox", nameof(RepositorySettingsViewModel.TemplateText), 120);
        text.TextWrapping = TextWrapping.NoWrap;
        text.SetBinding(IsEnabledProperty, new Binding(nameof(RepositorySettingsViewModel.CanEditTemplate)));
        p.Content(text, top: 8, indent: 0);
        p.Separator();
        p.Content(Form.Check("AddSignedOffMessageCheckBox", "Add Signed-off-by line at the end of the commit message", nameof(RepositorySettingsViewModel.SignOff)), top: 0, indent: 0);
        return p;
    }

    /// <summary>
    /// Issue Tracker：左に BugTrackerRulesListBox と + / −、右に選んだルールの欄。
    /// 何も選んでいないときは右に薄いアイコン（FallbackUserControl）。
    /// </summary>
    private FrameworkElement IssueTracker()
    {
        var vm = _vm.IssueRules;
        var p = new SettingsPage(narrowHeaders: true, indent: 0);
        p.Content(Form.Check("IsEnabledCheckbox", "Add hyperlinks to issue numbers in commit messages", nameof(RepositorySettingsViewModel.ShowBugtrackerLinks)), top: 0, indent: 0);
        var grid = new Grid { Height = 312, Margin = new Thickness(0, 10, 0, 0), DataContext = vm };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        // 左：一覧と + / −
        var left = new DockPanel();
        Button add = null!;
        add = PreferencesView.SmallButton("Icon.Plus", () =>
        {
            var menu = new ContextMenu { PlacementTarget = add, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            void Item(string id, string header, Action action)
            {
                var mi = new MenuItem { Header = header };
                AutomationProperties.SetAutomationId(mi, id);
                mi.Click += (_, _) => action();
                menu.Items.Add(mi);
            }
            Item("NewRuleMenuItem", "Add New Rule", vm.AddNew);
            Item("SampleGithubRuleMenuItem", "Add Sample Github Rule", () => vm.Add(Core.Settings.IssueTracker.SampleGitHub));
            Item("SampleJiraRuleMenuItem", "Add Sample Jira Rule", () => vm.Add(Core.Settings.IssueTracker.SampleJira));
            Item("SampleJiraMultiprojectRuleMenuItem", "Add Sample Jira Multiproject Rule", () => vm.Add(Core.Settings.IssueTracker.SampleJiraMultiproject));
            menu.IsOpen = true;
        });
        AutomationProperties.SetAutomationId(add, "AddRuleDropDownButton");
        var remove = PreferencesView.SmallButton("Icon.Minus", vm.RemoveSelected);
        AutomationProperties.SetAutomationId(remove, "RemoveRuleButton");
        remove.SetBinding(IsEnabledProperty, new Binding(nameof(IssueTrackerEditorViewModel.HasSelection)));
        var list = new ListBox { ItemsSource = vm.Rules, ItemTemplate = RuleTemplate() };
        AutomationProperties.SetAutomationId(list, "BugTrackerRulesListBox");
        list.SetResourceReference(ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(IssueTrackerEditorViewModel.Selected)) { Mode = BindingMode.TwoWay });
        left.Children.Add(PreferencesView.FramedList(list, add, remove));
        grid.Children.Add(left);

        // 右：選んだルール。選んでいないときは薄いアイコン
        var details = RuleDetails(vm);
        Grid.SetColumn(details, 1);
        details.SetBinding(VisibilityProperty, new Binding(nameof(IssueTrackerEditorViewModel.HasSelection)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        grid.Children.Add(details);
        var fallback = Fallback();
        Grid.SetColumn(fallback, 1);
        fallback.SetBinding(VisibilityProperty, new Binding(nameof(IssueTrackerEditorViewModel.HasSelection)) { Converter = Not.Instance });
        grid.Children.Add(fallback);
        p.Content(grid, top: 0, indent: 0);
        return p;
    }

    /// <summary>一覧の行：名前と、その下に淡く "Local" / "Shared"。</summary>
    private static DataTemplate RuleTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(4, 2, 4, 2));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(IssueRuleItemViewModel.Name)));
        panel.AppendChild(name);
        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetBinding(TextBlock.TextProperty, new Binding(nameof(IssueRuleItemViewModel.KindText)));
        kind.SetValue(TextBlock.FontSizeProperty, 11.0);
        kind.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        panel.AppendChild(kind);
        return new DataTemplate { VisualTree = panel };
    }

    private static FrameworkElement RuleDetails(IssueTrackerEditorViewModel vm)
    {
        var root = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var sel = new StackPanel();
        sel.SetBinding(DataContextProperty, new Binding(nameof(IssueTrackerEditorViewModel.Selected)));
        var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var nameLabel = new TextBlock { Text = "Name:", Width = 46, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(nameLabel, Dock.Left);
        nameRow.Children.Add(nameLabel);
        nameRow.Children.Add(Form.Text("NameTextBox", nameof(IssueRuleItemViewModel.Name)).WithoutAutoFocus());
        sel.Children.Add(nameRow);

        RadioButton Radio(string id, string text, string path)
        {
            // 組にしない（GroupName を分ける）：DataContext を替えたときに前の項目へ書き戻さないため（CustomCommandsEditorView と同じ）
            var r = new RadioButton { Content = text, GroupName = "it-" + id, Margin = new Thickness(0, 0, 14, 0) };
            r.SetResourceReference(ForegroundProperty, "Gk.Text");
            AutomationProperties.SetAutomationId(r, id);
            r.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path));
            return r;
        }
        var radios = Horizontal(Radio("LocalRadioButton", "Local", nameof(IssueRuleItemViewModel.IsLocal)), Radio("SharedRadioButton", "Shared", nameof(IssueRuleItemViewModel.IsShared)));
        radios.Margin = new Thickness(46, 2, 0, 0);
        sel.Children.Add(radios);
        var location = new TextBlock { Margin = new Thickness(46, 2, 0, 8) };
        location.SetBinding(TextBlock.TextProperty, new Binding(nameof(IssueRuleItemViewModel.LocationText)));
        location.SetResourceReference(ForegroundProperty, "Gk.Text.Dim");
        AutomationProperties.SetAutomationId(location, "RuleLocationTextBlock");
        sel.Children.Add(location);

        var regexHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var status = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(IssueRuleItemViewModel.StatusText)));
        status.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(IssueRuleItemViewModel.IsValid)) { Converter = ValidBrush.Instance });
        AutomationProperties.SetAutomationId(status, "RegexStatusTextBlock");
        DockPanel.SetDock(status, Dock.Right);
        regexHeader.Children.Add(status);
        regexHeader.Children.Add(new TextBlock { Text = "Parsing Regular Expression:" });
        sel.Children.Add(regexHeader);
        sel.Children.Add(Form.Text("RegexTextBox", nameof(IssueRuleItemViewModel.Regex)).Hint(@"e.g.'(\d+)'").WithoutAutoFocus());
        sel.Children.Add(new TextBlock { Text = "Result Url:", Margin = new Thickness(0, 10, 0, 4) });
        sel.Children.Add(Form.Text("UrlTextBox", nameof(IssueRuleItemViewModel.Url)).Hint("e.g.'https://github.com/project/issues/$1'").WithoutAutoFocus());
        var hint = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        hint.SetResourceReference(ForegroundProperty, "Gk.Text.Dim");
        var accent = (Brush)Application.Current.FindResource("Gk.Accent");
        hint.Inlines.Add("Access regex groups using ");
        hint.Inlines.Add(new Run("$1") { Foreground = accent });
        hint.Inlines.Add(", ");
        hint.Inlines.Add(new Run("$2") { Foreground = accent });
        hint.Inlines.Add(" etc variables");
        sel.Children.Add(hint);
        root.Children.Add(sel);

        // Test Message はルールを替えても残る
        root.Children.Add(new TextBlock { Text = "Test Message:", Margin = new Thickness(0, 14, 0, 4) });
        root.Children.Add(Form.Text("SampleMessageTextBox", nameof(IssueTrackerEditorViewModel.TestMessage)).WithoutAutoFocus());
        var preview = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        preview.SetResourceReference(ForegroundProperty, "Gk.Text.Dim");
        AutomationProperties.SetAutomationId(preview, "SamplePreviewTextBlock");
        void Update()
        {
            preview.Inlines.Clear();
            var message = vm.TestMessage;
            var links = vm.Selected is { } s ? Core.Settings.IssueTracker.FindLinks(message, [s.ToRule()]) : [];
            var at = 0;
            foreach (var l in links)
            {
                if (l.Start > at) preview.Inlines.Add(message[at..l.Start]);
                preview.Inlines.Add(new Run(message.Substring(l.Start, l.Length)) { Foreground = accent });
                at = l.Start + l.Length;
            }
            if (at < message.Length) preview.Inlines.Add(message[at..]);
        }
        vm.PropertyChanged += (_, _) => Update();
        Update();
        root.Children.Add(preview);
        return root;
    }

    /// <summary>RegexStatusTextBlock の色（valid は緑、invalid は赤）。</summary>
    private sealed class ValidBrush : IValueConverter
    {
        public static readonly ValidBrush Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => new SolidColorBrush(value is true ? Color.FromRgb(0x2E, 0xA0, 0x43) : Color.FromRgb(0xE5, 0x1A, 0x1A));
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Local / Shared のカスタムコマンド（窓は 946 幅）。</summary>
    private FrameworkElement CustomCommands() => new CustomCommandsEditorView(_vm.CustomCommands) { Margin = new Thickness(0, 6, 0, 4) };

    /// <summary>何も選んでいないときの右側（ナイフのロゴを薄く出す）。</summary>
    private static FrameworkElement Fallback()
    {
        var b = new Border();
        b.SetResourceReference(Border.BackgroundProperty, "Gk.Input.Background");
        AutomationProperties.SetAutomationId(b, "FallbackUserControl");
        var icon = new System.Windows.Controls.Image { Width = 88, Height = 88, Opacity = 0.25, Source = (System.Windows.Media.ImageSource)Application.Current.FindResource("Gk.AppIcon") };
        b.Child = icon;
        return b;
    }

    private static TextBlock Gravatar()
    {
        var link = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        link.Inlines.Add("Change the avatar at ");
        var hl = new Hyperlink(new Run("gravatar.com")) { NavigateUri = new Uri("https://gravatar.com") };
        hl.SetResourceReference(TextElement.ForegroundProperty, "Gk.Accent");
        hl.RequestNavigate += (_, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        link.Inlines.Add(hl);
        return link;
    }

    private sealed class Not : IValueConverter
    {
        public static readonly Not Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
