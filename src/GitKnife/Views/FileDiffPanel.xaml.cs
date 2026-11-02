using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GitKnife.Controls;
using GitKnife.Core.Diff;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>diff の表示の設定がどこのものか（画面ごとに Side by side / Inline を覚える）。</summary>
/// <summary>diff の置き場所。表示形式の設定が場所ごとに分かれている（RevisionWindow はコミットのポップアップと別ウィンドウ＝RevisionWindowDiffLayoutMode、Popup はファイル一覧の Space の大きな diff＝PopupDiffLayoutMode）。</summary>
public enum DiffPlace { LocalChanges, Revision, RevisionWindow, Popup }

public partial class FileDiffPanel : UserControl
{
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(nameof(Diff), typeof(FileDiff), typeof(FileDiffPanel),
        new PropertyMetadata(null, (d, e) => ((FileDiffPanel)d).OnDiffChanged((FileDiff?)e.NewValue)));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(FileDiffPanel),
        new PropertyMetadata("", (d, e) => ((FileDiffPanel)d).UpdateTitle()));
    public static readonly DependencyProperty IsStagedProperty = DependencyProperty.Register(nameof(IsStaged), typeof(bool), typeof(FileDiffPanel),
        new PropertyMetadata(false, (d, e) => ((FileDiffPanel)d).DiffView.IsStaged = (bool)e.NewValue));
    public static readonly DependencyProperty PlaceProperty = DependencyProperty.Register(nameof(Place), typeof(DiffPlace), typeof(FileDiffPanel),
        new PropertyMetadata(DiffPlace.LocalChanges, (d, e) => ((FileDiffPanel)d).ApplySettings()));

    public FileDiff? Diff { get => (FileDiff?)GetValue(DiffProperty); set => SetValue(DiffProperty, value); }

    /// <summary>画像の中身を読むリポジトリ。無ければ DataContext（Local Changes かコミット詳細）から。</summary>
    public RepositoryViewModel? Repository { get; set; }

    private void OnDiffChanged(FileDiff? diff)
    {
        var image = ImageDiffView.IsBinary(diff);
        var repo = Repository ?? (DataContext as LocalChangesViewModel)?.Repository ?? (DataContext as RevisionDetailsViewModel)?.Repository;
        var submodule = repo is not null && SubmoduleDiffView.IsSubmodule(diff);
        SubmoduleView.Visibility = submodule ? Visibility.Visible : Visibility.Collapsed;
        if (submodule)
        {
            NavButtons.Visibility = OptionButtons.Visibility = Visibility.Hidden;
            DiffView.Diff = null;
            DiffView.Visibility = ImageView.Visibility = Visibility.Collapsed;
            _ = SubmoduleView.LoadAsync(repo!, diff!);
            return;
        }
        // 画像のときは見出しの diff 用のボタンを隠す
        NavButtons.Visibility = OptionButtons.Visibility = image && repo is not null ? Visibility.Hidden : Visibility.Visible;
        if (image && repo is not null)
        {
            DiffView.Diff = null;
            DiffView.Visibility = Visibility.Collapsed;
            ImageView.Visibility = Visibility.Visible;
            _ = ImageView.LoadAsync(repo, diff!);
            return;
        }
        ImageView.Visibility = Visibility.Collapsed;
        DiffView.Visibility = Visibility.Visible;
        DiffView.Diff = diff;
    }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool IsStaged { get => (bool)GetValue(IsStagedProperty); set => SetValue(IsStagedProperty, value); }
    public DiffPlace Place { get => (DiffPlace)GetValue(PlaceProperty); set => SetValue(PlaceProperty, value); }

    /// <summary>文脈の行数や Entire File を変えた（diff を読み直してほしい）。</summary>
    public event EventHandler? OptionsChanged;
    public event EventHandler<DiffLinesEventArgs>? StageRequested
    {
        add => DiffView.StageRequested += value;
        remove => DiffView.StageRequested -= value;
    }
    public event EventHandler<DiffLinesEventArgs>? UnstageRequested
    {
        add => DiffView.UnstageRequested += value;
        remove => DiffView.UnstageRequested -= value;
    }
    public event EventHandler<DiffLinesEventArgs>? DiscardRequested
    {
        add => DiffView.DiscardRequested += value;
        remove => DiffView.DiscardRequested -= value;
    }

    public FileDiffPanel()
    {
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new RelayAction(() => DiffView.GoToChange(-1)), Key.Up, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayAction(() => DiffView.GoToChange(1)), Key.Down, ModifierKeys.Control));
        Loaded += (_, _) => ApplySettings();
        UpdateTitle();
    }

    /// <summary>コミット詳細ではステージのボタンを出さない。</summary>
    public bool ShowActions { get => DiffView.ShowActions; set => DiffView.ShowActions = value; }

    private int LayoutMode
    {
        get => Place switch
        {
            DiffPlace.Revision => App.Settings.RevisionDiffLayoutMode,
            DiffPlace.RevisionWindow => App.Settings.RevisionWindowDiffLayoutMode,
            DiffPlace.Popup => App.Settings.PopupDiffLayoutMode,
            _ => App.Settings.CommitDiffLayoutMode,
        };
        set
        {
            switch (Place)
            {
                case DiffPlace.Revision: App.Settings.RevisionDiffLayoutMode = value; break;
                case DiffPlace.RevisionWindow: App.Settings.RevisionWindowDiffLayoutMode = value; break;
                case DiffPlace.Popup: App.Settings.PopupDiffLayoutMode = value; break;
                default: App.Settings.CommitDiffLayoutMode = value; break;
            }
        }
    }

    private void ApplySettings()
    {
        if (App.Settings is null) return;
        EntireFileToggle.IsChecked = App.Settings.DiffShowEntireFile;
        IgnoreWhitespaceToggle.IsChecked = App.Settings.DiffIgnoreWhitespaces;
        WordWrapToggle.IsChecked = App.Settings.DiffWordWrap;
        DiffView.WordWrap = App.Settings.DiffWordWrap;
        LayoutToggle.IsChecked = LayoutMode == 1;
        DiffView.SideBySide = LayoutMode == 1;
    }

    private void UpdateTitle()
    {
        FilePathTextBlock.Text = Title;
        FileTypeImage.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>今の設定での文脈の行数（Entire File なら全体）と、空白を無視するか（`--ignore-all-space`）。</summary>
    public static Core.Git.Commands.DiffContext ContextLines =>
        new(App.Settings.DiffShowEntireFile ? 100000 : App.Settings.DiffContextSize, App.Settings.DiffIgnoreWhitespaces);

    private void Previous_Click(object sender, RoutedEventArgs e) => DiffView.GoToChange(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => DiffView.GoToChange(1);
    private void HiddenSymbols_Click(object sender, RoutedEventArgs e) => DiffView.ShowHiddenSymbols = ((ToggleButton)sender).IsChecked == true;
    private void FewerLines_Click(object sender, RoutedEventArgs e) => ChangeContext(-1);
    private void MoreLines_Click(object sender, RoutedEventArgs e) => ChangeContext(1);

    /// <summary>文脈の行数を増減する（設定 DiffContextSize。既定 19）。</summary>
    private void ChangeContext(int direction)
    {
        int[] steps = [0, 1, 3, 6, 12, 19, 25, 50, 100];
        var current = App.Settings.DiffContextSize;
        App.Settings.DiffContextSize = direction > 0 ? steps.FirstOrDefault(s => s > current, steps[^1]) : steps.LastOrDefault(s => s < current, 0);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void IgnoreWhitespace_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DiffIgnoreWhitespaces = IgnoreWhitespaceToggle.IsChecked == true;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WordWrap_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DiffWordWrap = WordWrapToggle.IsChecked == true;
        DiffView.WordWrap = App.Settings.DiffWordWrap;
    }

    private void EntireFile_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DiffShowEntireFile = EntireFileToggle.IsChecked == true;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        LayoutMode = LayoutToggle.IsChecked == true ? 1 : 0;
        DiffView.SideBySide = LayoutMode == 1;
    }
}
