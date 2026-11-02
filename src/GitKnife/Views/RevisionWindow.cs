using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using GitKnife.ViewModels;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// コミット詳細の別ウィンドウ。
/// ポップアップの "Open in Separate Window" とコミット詳細の ShowRevisionInSeparateWindowButton から開く。押すたびに新しい窓（複数開ける）。
/// メインと別のトップレベル窓で、題名は "9401224 Capitalize greeting"。上に PART_MinimizeButton / PART_MaximizeButton / PART_CloseButton、
/// その下にコミット詳細（見出しがタブより上）。位置は閉じたときに RevisionWindowLocationState に書き、次はそこに開く。
/// </summary>
internal sealed class RevisionWindow : Window
{
    private readonly Button _restore;
    private readonly Button _maximize;

    public static void Open(RepositoryViewModel repo, CommitRow row, DetailsMode mode, string? selectedPath)
        => new RevisionWindow(new RevisionWindowViewModel(repo, row, mode, selectedPath)).Show();

    /// <summary>
    /// History / Blame の行の "Open in Separate Window..." と ⇗ のボタン。
    /// 一覧に読み込んでいないコミットは、行の情報だけで仮の行を作る（PARENTS は出ない）。
    /// </summary>
    public static void OpenCommit(RepositoryViewModel repo, string sha, string author, string email, DateTimeOffset date, string subject)
    {
        var row = repo.Commits.FirstOrDefault(c => c.Commit.Sha == sha) ?? new CommitRow
        {
            Commit = new Core.Git.Model.CommitInfo(sha, [], author, email, date, author, email, date, subject, ""),
            Graph = new Core.Graph.GraphRow { NodeLane = 0, NodeColor = 0, Segments = [], Width = 1 },
            Labels = [],
        };
        Open(repo, row, DetailsMode.Commit, null);
    }

    private RevisionWindow(RevisionWindowViewModel vm)
    {
        DataContext = vm;
        Title = vm.Title;
        Width = 1000;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 26, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
        SetResourceReference(BackgroundProperty, "Gk.Details.Background");
        SetResourceReference(ForegroundProperty, "Gk.Text");
        SetResourceReference(FontFamilyProperty, "Gk.UiFont");
        FontSize = 12.5;

        var root = new DockPanel();
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Height = 26 };
        WindowChrome.SetIsHitTestVisibleInChrome(caption, true);
        caption.Children.Add(CaptionButton("PART_MinimizeButton", "Icon.Minimize", "Gk.CaptionButton", () => WindowState = WindowState.Minimized));
        _restore = CaptionButton("PART_RestoreButton", "Icon.Restore", "Gk.CaptionButton", () => WindowState = WindowState.Normal);
        _maximize = CaptionButton("PART_MaximizeButton", "Icon.Maximize", "Gk.CaptionButton", () => WindowState = WindowState.Maximized);
        caption.Children.Add(_restore);
        caption.Children.Add(_maximize);
        caption.Children.Add(CaptionButton("PART_CloseButton", "Icon.Close", "Gk.CloseCaptionButton", Close));
        var bar = new Border { Child = caption };
        bar.SetResourceReference(Border.BackgroundProperty, "Gk.Header.Background");
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new RevisionDetailsView { IsStandalone = true });
        Content = new Border { Child = root, BorderThickness = new Thickness(1) }.With(b => b.SetResourceReference(Border.BorderBrushProperty, "Gk.Border"));

        SourceInitialized += (_, _) => RestoreLocation();
        StateChanged += (_, _) => UpdateButtons();
        Closing += (_, _) => SaveLocation();
        UpdateButtons();
    }

    private Button CaptionButton(string id, string icon, string style, Action click)
    {
        var path = new Path { Data = (Geometry)FindResource(icon), StrokeThickness = 1 };
        path.SetResourceReference(StyleProperty, "Gk.IconPath");
        var b = new Button { Content = path };
        b.SetResourceReference(StyleProperty, style);
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => click();
        return b;
    }

    private void UpdateButtons()
    {
        _restore.Visibility = WindowState == WindowState.Maximized ? Visibility.Visible : Visibility.Collapsed;
        _maximize.Visibility = WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
        BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    /// <summary>
    /// 保存した位置に開く。空なら既定（100,100 の 1000×600）。
    /// </summary>
    private void RestoreLocation()
    {
        var s = App.Settings.RevisionWindowLocationState;
        var rect = s.Width >= 300 && s.Height >= 200 ? new Rect(s.Left, s.Top, s.Width, s.Height) : new Rect(100, 100, 1000, 600);
        if (!rect.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)))
            rect = new Rect(100, 100, 1000, 600);
        Left = rect.Left; Top = rect.Top; Width = rect.Width; Height = rect.Height;
        if (s.WindowState == (int)WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveLocation()
    {
        var s = App.Settings.RevisionWindowLocationState;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.Left = (int)r.Left;
        s.Top = (int)r.Top;
        s.Width = (int)r.Width;
        s.Height = (int)r.Height;
        s.WindowState = WindowState == WindowState.Maximized ? (int)WindowState.Maximized : 0;
    }
}
