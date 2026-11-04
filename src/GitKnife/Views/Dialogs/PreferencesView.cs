using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using GitKnife.Controls;
using GitKnife.Core.Diff;
using GitKnife.ViewModels.Dialogs;
using static GitKnife.Views.Dialogs.Form;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Preferences の本文。
/// タブごとに左に太字の見出し（"Source Code Folder"、"Diff View"…）、右寄せのラベル、入力列。Integration だけ幅が広い（696）。
/// </summary>
internal sealed class PreferencesView : UserControl
{
    private readonly PreferencesViewModel _vm;

    public PreferencesView(PreferencesViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        var tabs = new TabControl();
        tabs.SetResourceReference(StyleProperty, "Gk.TabControl");
        tabs.Width = 532;
        tabs.Margin = new Thickness(0, 6, 0, 0);
        tabs.Items.Add(Tab("General", "GeneralUserControl", General()));
        tabs.Items.Add(Tab("Commit", "CommitUserControl", Commit()));
        tabs.Items.Add(Tab("Git", "GitUserControl", Git()));
        tabs.Items.Add(Tab("Integration", "IntegrationUserControl", Integration()));
        tabs.Items.Add(Tab("Custom Commands", "CustomCommandsUserControl", CustomCommands()));
        tabs.Items.Add(Tab("Updates", "UpdatesUserControl", Updates()));
        tabs.Items.Add(Tab("Hosts", "HostsUserControl", Hosts()));   // GitKnife だけ（T1301）
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || Window.GetWindow(this) is not { } w) return;
            // Integration と Custom Commands は広い。SizeToContent が Height のままだと Width を入れても戻されるので、いったん Manual にする
            tabs.Width = tabs.SelectedIndex switch { 3 => 654, 4 => 904, _ => 532 };
            w.SizeToContent = SizeToContent.Manual;
            w.Width = tabs.Width + 42;
            w.SizeToContent = SizeToContent.Height;
        };
        Content = tabs;
        Loaded += async (_, _) => await vm.LoadGitAsync();
    }

    private static TabItem Tab(string header, string id, FrameworkElement body)
    {
        var t = new TabItem { Header = header };
        t.SetResourceReference(StyleProperty, "Gk.TabItem");
        AutomationProperties.SetAutomationId(body, id);
        // 本文の文字は太字にしない（TabItem の FontWeight を継がない）
        body.SetValue(TextElement.FontWeightProperty, FontWeights.Normal);
        t.Content = body;
        return t;
    }

    // ------------------------------------------------------------------ General

    private FrameworkElement General()
    {
        var p = new SettingsPage();

        // Source Code Folder：フォルダの一覧と + / フォルダを開くボタン
        var list = new ListBox { Height = 52 };
        AutomationProperties.SetAutomationId(list, "SrcDirsListBox");
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(PreferencesViewModel.SourceDirectories)));
        list.ItemTemplate = SourceDirTemplate();
        list.SetResourceReference(BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(BorderBrushProperty, "Gk.Input.Border");
        var add = SmallButton("Icon.Plus", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select source code folder" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) _vm.AddSourceDirectory(dlg.FolderName);
        });
        // 行の右端のボタン：エクスプローラーで開く
        list.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button { Tag: string dir } && System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;   // 右端のボタンを行の右に置く
        list.SetResourceReference(ForegroundProperty, "Gk.Text");
        var remove = SmallButton("Icon.Minus", () => { if (list.SelectedItem is string s) _vm.RemoveSourceDirectory(s); });
        AutomationProperties.SetAutomationId(remove, "RemoveSrcDirButton");
        var framed = FramedList(list, add, remove);
        framed.Height = 70;   // 一覧 52 と帯
        // − は選んでいるときだけ
        ((FrameworkElement)remove.Parent).SetBinding(VisibilityProperty, new Binding(nameof(ListBox.SelectedItem)) { Source = list, Converter = NotNullToVisibility.Instance });
        p.Section("Source Code Folder", framed);
        p.Separator();

        // Diff View：見本の diff、"Show -+ marks"、"Font size:"、"Disable syntax highlighting"
        var preview = new DiffViewer { SideBySide = false, ShowActions = false, Height = 110 };
        AutomationProperties.SetAutomationId(preview, "DiffCodeEditor");
        preview.Diff = UnifiedDiffParser.Parse(SampleDiff).FirstOrDefault();
        var size = Number("CodeEditorFontSizeTextBox", nameof(PreferencesViewModel.CodeEditorFontSize));
        var sizeRow = Horizontal(Check("ShowDiffChangeMarksCheckBox", "Show -+ marks", nameof(PreferencesViewModel.ShowChangeMarks)),
            new TextBlock { Text = "Font size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) }, size);
        sizeRow.Margin = new Thickness(0, 8, 0, 0);
        var syntax = Check("DisableSyntaxHighlightingCheckBox", "Disable syntax highlighting", nameof(PreferencesViewModel.DisableSyntaxHighlighting));
        syntax.Margin = new Thickness(0, 9, 0, 0);
        p.Section("Diff View", new StackPanel { Children = { preview, sizeRow, syntax } });
        p.Separator();

        p.Row("Sort commits:", Horizontal(
            Radio("TopologicalSortOrderRadioButton", "Topologically", "sort", nameof(PreferencesViewModel.SortOrder), "Topological"),
            Radio("DateSortOrderRadioButton", "By date", "sort", nameof(PreferencesViewModel.SortOrder), "Date")));
        p.Separator();

        p.Check("FetchRemotesAutomaticallyCheckBox", "Fetch remotes automatically", nameof(PreferencesViewModel.FetchRemotesAutomatically));
        p.Check("FetchAllTagsCheckBox", "Always include tags into fetch", nameof(PreferencesViewModel.FetchAllTags));
        p.Check("UpdateRepoStatusAutomaticallyCheckBox", "Tab indicator for uncommitted changes", nameof(PreferencesViewModel.UpdateRepoStatusAutomatically));
        p.Check("UpdateSubmodulesAutomaticallyCheckBox", "Update submodules automatically on checkout", nameof(PreferencesViewModel.UpdateSubmodulesOnCheckout));
        var space = Combo("SpaceCharacterComboBox", nameof(PreferencesViewModel.SpaceCharacters), nameof(PreferencesViewModel.SpaceCharacter));
        space.Width = 40;
        p.Content(Horizontal(new TextBlock { Text = "Space character in branch names:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, space), top: 2);
        p.Check("PushAutomaticallyOnCommitCheckBox", "Push automatically on commit", nameof(PreferencesViewModel.PushAutomaticallyOnCommit));
        p.Check("CompactBranchLabelsCheckBox", "Compact branch labels", nameof(PreferencesViewModel.CompactBranchLabels));
        return p;
    }

    private const string SampleDiff = """
        diff --git a/Sample.swift b/Sample.swift
        --- a/Sample.swift
        +++ b/Sample.swift
        @@ -1,3 +1,3 @@
         public func elementsEqual<OtherSequence>(
         _ other: OtherSequence,
        -isEquivalent isEquivalent: (${GElement}, ${GElement}) throws -> Bool
        +isEquivalent: (${GElement}, ${GElement}) throws -> Bool
        """;

    /// <summary>
    /// Source Code Folder の行：エクスプローラーのフォルダのアイコン、repositories.toml の source_dirs のままのパス（末尾に "\" の付いた 'D:\Proj\'）、
    /// 右端のボタン（エクスプローラーで開く）。
    /// </summary>
    private static DataTemplate SourceDirTemplate()
        => (DataTemplate)System.Windows.Markup.XamlReader.Parse($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:c="clr-namespace:GitKnife.Controls;assembly=GitKnife">
                <Grid Height="20">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <Image Source="{x:Static c:ShellIconConverter.Folder}" Width="16" Height="16" Margin="2,0,6,0" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1" Text="{Binding}" VerticalAlignment="Center"/>
                    <Button Grid.Column="2" Width="18" Height="16" Margin="4,0,2,0" Style="{DynamicResource Gk.HeaderIconButton}" Tag="{Binding}" ToolTip="Show in File Explorer">
                        <c:IconImage Geometry="{DynamicResource Icon.Folder}" Width="12" Height="12"/>
                    </Button>
                </Grid>
            </DataTemplate>
            """);

    // ------------------------------------------------------------------ Commit

    private FrameworkElement Commit()
    {
        var p = new SettingsPage();
        p.Header("Commit Message");
        var low = Number("CommitSubjectLowLimitTextBox", nameof(PreferencesViewModel.CommitSubjectLowLimit));
        var high = Number("CommitSubjectHighLimitTextBox", nameof(PreferencesViewModel.CommitSubjectHighLimit));
        p.Row("Length indicator:", Horizontal(
            new TextBlock { Text = "Low limit:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, low,
            new TextBlock { Text = "High limit:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) }, high), labelWidth: 200);
        var spell = Combo("CommintSpellCheckingComboBox", nameof(PreferencesViewModel.SpellCheckingModes), nameof(PreferencesViewModel.SpellCheckingMode));
        spell.Width = 132;
        spell.HorizontalAlignment = HorizontalAlignment.Left;
        p.Row("Spell checking:", spell, labelWidth: 200);
        var guide = Number("PageGuideLinePositionTextBox", nameof(PreferencesViewModel.PageGuideLinePosition));
        guide.HorizontalAlignment = HorizontalAlignment.Left;
        p.Row("Page guide at column:", guide, labelWidth: 200);
        p.Separator();
        p.Header("Generate Commit Message");
        var login = new Button { Content = "Log In...", Padding = new Thickness(4, 1, 4, 1), HorizontalAlignment = HorizontalAlignment.Left };
        login.SetResourceReference(StyleProperty, "Gk.Button");
        login.MinWidth = 0;
        AutomationProperties.SetAutomationId(login, "OpenAiLoginButton");
        login.Click += (_, _) => Alerts.NotImplemented("Log In...");
        p.Row("OpenAI:", login, labelWidth: 200);
        return p;
    }

    // ------------------------------------------------------------------ Git

    private FrameworkElement Git()
    {
        var p = new SettingsPage();
        var instance = Combo("GitInstanceComboBox", nameof(PreferencesViewModel.GitInstances), nameof(PreferencesViewModel.SelectedGitInstance));
        p.Row("Git Instance:", instance, labelWidth: 160);
        var verbose = Check("VerboseGitOutputCheckBox", "Enable verbose Git output", nameof(PreferencesViewModel.VerboseGitOutput));
        p.Row(null, verbose, labelWidth: 160);
        p.Separator();
        p.Header("Global User Information");
        var avatar = new Avatar { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0) };
        AutomationProperties.SetAutomationId(avatar, "AuthorAvatarImage");
        avatar.SetBinding(Avatar.NameTextProperty, new Binding(nameof(PreferencesViewModel.UserName)));
        avatar.SetBinding(Avatar.EmailProperty, new Binding(nameof(PreferencesViewModel.Email)));
        var name = Text("UserNameTextBox", nameof(PreferencesViewModel.UserName));
        var email = Text("EmailTextBox", nameof(PreferencesViewModel.Email));
        email.Margin = new Thickness(0, 8, 0, 0);
        var link = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        link.Inlines.Add("Change the avatar at ");
        var hl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("gravatar.com")) { NavigateUri = new Uri("https://gravatar.com") };
        hl.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Gk.Accent");
        hl.RequestNavigate += (_, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        link.Inlines.Add(hl);
        var fields = new StackPanel { Children = { name, email, link } };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(avatar);
        Grid.SetColumn(fields, 1);
        grid.Children.Add(fields);
        grid.Margin = new Thickness(0, 0, 0, 0);
        p.Row(null, grid, labelWidth: 160);
        return p;
    }

    // ------------------------------------------------------------------ Integration

    private FrameworkElement Integration()
    {
        var p = new SettingsPage(narrowHeaders: true);
        p.Header("Issue Tracker Integration");
        p.Row(null, Check("ShowBugtrackerLinksCheckBox", "Highlight issue tracker links in commit messages", nameof(PreferencesViewModel.ShowBugtrackerLinks)), labelWidth: 170);
        p.Header("External Merge Tools");
        p.Content(ToolsEditor("ExternalMergeToolsUserControl", nameof(PreferencesViewModel.MergeTools), nameof(PreferencesViewModel.SelectedMergeTool)), top: 4, indent: 0);
        p.Header("External Diff Tools");
        p.Content(ToolsEditor("ExternalDiffToolsUserControl", nameof(PreferencesViewModel.DiffTools), nameof(PreferencesViewModel.SelectedDiffTool)), top: 4, indent: 0);
        p.Header("Shell");
        var shell = Combo("ShellComboBox", nameof(PreferencesViewModel.Shells), nameof(PreferencesViewModel.Shell));
        p.Row("Shell:", shell, labelWidth: 170);
        p.Row("Shell Path:", WithBrowse(Text("ShellPathTextBox", nameof(PreferencesViewModel.ShellPath))), labelWidth: 170);
        p.Row("Arguments:", Text("ShellArgumentsTextBox", nameof(PreferencesViewModel.ShellArguments)), labelWidth: 170);
        return p;
    }

    /// <summary>外部ツールの一覧と、選んだツールの Name / Path / Arguments。</summary>
    private FrameworkElement ToolsEditor(string id, string itemsPath, string selectedPath)
    {
        var grid = new Grid { Height = 102 };
        AutomationProperties.SetAutomationId(grid, id);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        // 行はアイコンと名前（21px の行）。下に + / −（− は決まったツールでは無効）
        var list = new ListBox { Height = 84, VerticalAlignment = VerticalAlignment.Top };
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Orientation="Horizontal" Height="19">
                    <Image Source="{Binding Icon}" Width="16" Height="16" Margin="0,0,6,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                </StackPanel>
            </DataTemplate>
            """);
        AutomationProperties.SetAutomationId(list, "ToolsListBox");
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(selectedPath));
        list.SetResourceReference(BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(BorderBrushProperty, "Gk.Input.Border");
        list.SetResourceReference(ForegroundProperty, "Gk.Text");
        var add = SmallButton("Icon.Plus", () => Alerts.NotImplemented("Add external tool"));
        var remove = SmallButton("Icon.Minus", () => { });
        remove.IsEnabled = false;   // 決まったツールは消せない
        grid.Children.Add(FramedList(list, add, remove));

        var card = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Gk.Input.Background");
        var form = new Form(76);
        form.Row("Name:", Value(null, selectedPath + "." + nameof(ExternalToolEntry.Name)), height: 30);
        form.Row("Path:", WithBrowse(Text("ToolPathTextBox", selectedPath + "." + nameof(ExternalToolEntry.Path))), height: 30);
        var args = Text("", selectedPath + "." + nameof(ExternalToolEntry.Arguments));
        var hint = new TextBlock { Text = "?", Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "$LOCAL, $REMOTE, $BASE, $MERGED" };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Accent");
        AutomationProperties.SetAutomationId(hint, "ArgumentsHintTextBlock");
        var argsRow = new DockPanel();
        DockPanel.SetDock(hint, Dock.Right);
        argsRow.Children.Add(hint);
        argsRow.Children.Add(args);
        form.Row("Arguments:", argsRow, height: 30);
        card.Child = form;
        Grid.SetColumn(card, 2);
        grid.Children.Add(card);
        return grid;
    }

    internal static FrameworkElement WithBrowse(TextBox box)
    {
        var dock = new DockPanel();
        var browse = new Button { Width = 23, Height = 23, Margin = new Thickness(4, 0, 0, 0), Content = new IconImage { Geometry = Icon("Icon.Folder") } };
        browse.SetResourceReference(StyleProperty, "Gk.HeaderIconButton");
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe|All files|*.*" };
            if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(box);
        return dock;
    }

    // ------------------------------------------------------------------ Custom Commands / Updates

    /// <summary>グローバルのカスタムコマンド。</summary>
    private FrameworkElement CustomCommands() => new CustomCommandsEditorView(_vm.CustomCommands) { Margin = new Thickness(0, 6, 0, 4) };

    private FrameworkElement Updates()
    {
        var p = new SettingsPage();
        var combo = Combo("AutomaticUpdateComboBox", nameof(PreferencesViewModel.UpdateTypes), nameof(PreferencesViewModel.UpdateType));
        combo.Width = 278;
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        p.Row("Automatic Updates:", combo, labelWidth: 170);
        return p;
    }

    // ------------------------------------------------------------------ Hosts（GitKnife だけ。T1301）

    /// <summary>
    /// SSH のホストの一覧と、選んだホストの名前・接続先・git の場所、Test Connection。WSL はパスで決まるので一覧を出すだけ。
    /// SSH のリポジトリは File > Open Repository... で開く（パスは \\gitknife-ssh\&lt;名前&gt;\…）。ホストはそこの New Host... でも足せる。
    /// </summary>
    private FrameworkElement Hosts()
    {
        var p = new SettingsPage();
        var list = new ListBox { DisplayMemberPath = nameof(SshHostEntry.Label) };
        AutomationProperties.SetAutomationId(list, "SshHostsListBox");
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(PreferencesViewModel.SshHosts)));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(PreferencesViewModel.SelectedSshHost)) { Mode = BindingMode.TwoWay });
        list.SetResourceReference(ForegroundProperty, "Gk.Text");
        var add = SmallButton("Icon.Plus", _vm.AddSshHost);
        AutomationProperties.SetAutomationId(add, "AddSshHostButton");
        var remove = SmallButton("Icon.Minus", _vm.RemoveSshHost);
        AutomationProperties.SetAutomationId(remove, "RemoveSshHostButton");
        var framed = FramedList(list, add, remove);
        framed.Height = 90;
        p.Section("SSH Hosts", framed);

        var fields = new StackPanel();
        fields.SetBinding(VisibilityProperty, new Binding(nameof(PreferencesViewModel.SelectedSshHost)) { Converter = NotNullToVisibility.Instance });
        var detail = new SettingsPage();
        detail.Row("Name:", Text("SshHostNameTextBox", "SelectedSshHost.Name"));
        detail.Row("Destination:", Text("SshHostDestinationTextBox", "SelectedSshHost.Destination"));
        detail.Row(null, Note("user@host or a Host in ~/.ssh/config. Empty uses the name."));
        detail.Row("Git:", Text("SshHostGitTextBox", "SelectedSshHost.GitPath"));
        detail.Row(null, Note("Path of git on the host. Empty uses git on PATH."));
        var test = new Button { Content = "Test Connection", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(test, "TestSshHostButton");
        test.Click += async (_, _) => await _vm.TestSshHostAsync();
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreferencesViewModel.SshTestStatus)));
        var forget = new Button { Content = "Forget Password", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(forget, "ForgetSshPasswordButton");
        forget.Click += (_, _) => _vm.ForgetSshPassword();
        detail.Row(null, Horizontal(test, forget, status));
        detail.Row(null, Note("Passwords and key passphrases are asked when connecting, and can be saved in Windows Credential Manager."));
        fields.Children.Add(detail);
        p.Children.Add(fields);
        p.Separator();

        var wsl = new TextBlock { TextWrapping = TextWrapping.Wrap };
        wsl.SetBinding(TextBlock.TextProperty, new Binding(nameof(PreferencesViewModel.WslDistributions)));
        p.Section("WSL", new StackPanel { Children = { wsl, Note("Repositories under \\\\wsl.localhost\\<distro>\\ (or a drive mapped to it) use git inside that distribution. No setup is needed.") } });
        Loaded += async (_, _) => await _vm.LoadWslAsync();
        return p;
    }

    /// <summary>数字の小さな入力欄（"Font size:"、"Low limit:" など）。</summary>
    internal static TextBox Number(string id, string path)
    {
        var t = Text(id, path);
        t.Width = 32;
        t.Height = 22;
        t.Padding = new Thickness(2, 0, 2, 0);
        return t;
    }

    /// <summary>
    /// 枠の中に一覧と、下の帯の + / − を入れる（Issue Tracker・Custom Commands・外部ツール・Source Code Folder の一覧。
    /// 枠と区切りは #525252、一覧の地 #212121、帯 #333333、ボタンは 22px 幅で地 #373737、間に縦線）。
    /// 2 つ目からのボタンは左にも縦線があり、間の線は 2 本並ぶ（+ の地 22..43、線 44・45、- の地 46..67、線 68）。
    /// </summary>
    internal static FrameworkElement FramedList(ListBox list, params Button[] buttons)
    {
        list.BorderThickness = new Thickness(0);
        list.SetResourceReference(BackgroundProperty, "Gk.FramedList.Background");
        list.VerticalAlignment = VerticalAlignment.Stretch;
        list.ClearValue(HeightProperty);
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            b.Width = 22;
            b.Height = 21;
            b.Margin = new Thickness(0);
            if (b.Content is IconImage icon)
            {
                icon.SetResourceReference(IconImage.StrokeProperty, "Gk.FramedList.Icon");
                // 22px の中に 12px を置くと線が画素の境目にまたがってぼやける。半画素ずらして 1 列に乗せる（+ / - をくっきり 1px に）
                icon.RenderTransform = new System.Windows.Media.TranslateTransform(-0.5, 0.3);
            }
            var cell = new Border { BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 1, 0), Child = b };
            cell.SetResourceReference(Border.BorderBrushProperty, "Gk.Input.Border");
            cell.SetResourceReference(Border.BackgroundProperty, "Gk.FramedList.Button");
            bar.Children.Add(cell);
        }
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
        footer.SetResourceReference(Border.BorderBrushProperty, "Gk.Input.Border");
        footer.SetResourceReference(Border.BackgroundProperty, "Gk.FramedList.Footer");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(footer, 1);
        grid.Children.Add(list);
        grid.Children.Add(footer);
        var frame = new Border { BorderThickness = new Thickness(1), Child = grid };
        frame.SetResourceReference(Border.BorderBrushProperty, "Gk.Input.Border");
        return frame;
    }

    internal static Button SmallButton(string icon, Action action)
    {
        var b = new Button { Width = 18, Height = 18, Content = new IconImage { Geometry = Icon(icon) } };
        b.SetResourceReference(StyleProperty, "Gk.HeaderIconButton");
        b.Click += (_, _) => action();
        return b;
    }

    internal sealed class NotNullToVisibility : IValueConverter
    {
        public static readonly NotNullToVisibility Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>
/// 1 タブの中身。左の列に太字の見出しかラベル（右寄せ）、右の列に入力。区切り線は見出しの位置から右端まで。
/// </summary>
internal sealed class SettingsPage : StackPanel
{
    private readonly bool _narrowHeaders;
    private readonly double _indent;

    public SettingsPage(bool narrowHeaders = false, double indent = 147)
    {
        _narrowHeaders = narrowHeaders;
        _indent = indent;
        Margin = new Thickness(0, 0, 0, 4);
    }

    public void Header(string text)
        => Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(_narrowHeaders ? 0 : 14, 8, 0, 6) });

    /// <summary>見出しを左に置き、同じ高さから右の列に中身。</summary>
    public void Section(string header, FrameworkElement content)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_indent) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(14, 0, 0, 0) });
        content.Margin = new Thickness(content.Margin.Left, 18, 14, 0);
        Grid.SetColumn(content, 1);
        g.Children.Add(content);
        Children.Add(g);
    }

    /// <summary>右寄せのラベル + 入力の 1 行（label が null なら入力列だけ）。</summary>
    public void Row(string? label, FrameworkElement element, double labelWidth = 0)
    {
        if (labelWidth == 0) labelWidth = _indent;
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        if (label is not null)
            g.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = element is Grid ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, 1);
        g.Children.Add(element);
        Children.Add(g);
    }

    public void Check(string id, string text, string path) => Content(Form.Check(id, text, path), top: 8);

    /// <summary>右の列に入れる（チェックボックスの縦の並びなど）。</summary>
    public void Content(FrameworkElement element, double top, double? indent = null)
    {
        element.Margin = new Thickness(indent ?? _indent, top, 0, 0);
        element.HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(element);
    }

    public void Separator(double top = 10)
    {
        var line = new Border { Height = 1, Margin = new Thickness(_narrowHeaders ? 0 : 14, top, 0, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        Children.Add(line);
    }
}
