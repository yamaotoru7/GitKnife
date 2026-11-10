using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using GitKnife.ViewModels;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// コミット一覧の Space で開くコミット詳細のポップアップ。
/// メインの窓が持ち主の枠なしの窓（題名 "7cabd6e Add changelog"、既定 1000×600、位置は RevisionPopUpWindowLocationState）。
/// 上の帯に "Open in Separate Window" と閉じるボタン、その下にコミット詳細（見出しがタブより上）。Esc で閉じる。
/// </summary>
internal sealed class RevisionPopupWindow : Window
{
    public RevisionPopupWindow(RepositoryViewModel repo, CommitRow row)
    {
        var vm = new RevisionWindowViewModel(repo, row);
        DataContext = vm;
        Title = vm.Title;
        Width = 1000;
        Height = 600;
        ShowInTaskbar = false;
        ShowActivated = false;   // 開いても一覧のフォーカスはそのまま（開いた直後の Esc や上下キーは一覧に行く）
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 28, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = WindowFrame.GlassThickness, CornerRadius = new CornerRadius(0) });
        SetResourceReference(BackgroundProperty, "Gk.Details.Background");
        SetResourceReference(ForegroundProperty, "Gk.Text");
        SetResourceReference(FontFamilyProperty, "Gk.UiFont");
        FontSize = 12.5;

        var root = new DockPanel();
        var bar = new DockPanel { Height = 28, LastChildFill = false };
        bar.SetResourceReference(Panel.BackgroundProperty, "Gk.Header.Background");
        DockPanel.SetDock(bar, Dock.Top);

        var close = new Button { Content = IconPath("Icon.Close") };
        close.SetResourceReference(StyleProperty, "Gk.CloseCaptionButton");
        AutomationProperties.SetAutomationId(close, "PART_CloseButton");
        close.Click += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);

        // 押すとポップアップを閉じ、今のモードと選んでいるファイルのまま別ウィンドウで開く
        var separate = new Button { Content = "Open in Separate Window", Padding = new Thickness(10, 2, 10, 2), Height = 24, Margin = new Thickness(0, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
        separate.SetResourceReference(StyleProperty, "Gk.Button");
        separate.Click += (_, _) =>
        {
            RevisionWindow.Open(repo, row, vm.DetailsMode, vm.Details.SelectedChange?.Path);
            Close();
        };
        WindowChrome.SetIsHitTestVisibleInChrome(separate, true);
        DockPanel.SetDock(separate, Dock.Right);
        bar.Children.Add(separate);
        root.Children.Add(bar);

        root.Children.Add(new RevisionDetailsView { IsStandalone = true });
        Content = new Border { Child = root, BorderThickness = new Thickness(0) }.With(b => b.SetResourceReference(Border.BorderBrushProperty, "Gk.Border"));

        InputBindings.Add(new KeyBinding(new RelayAction(Close), Key.Escape, ModifierKeys.None));
        SourceInitialized += (_, _) => RestoreLocation();
        Closing += (_, _) => SaveLocation();
    }

    private Path IconPath(string key)
    {
        var path = new Path { Data = (Geometry)FindResource(key), StrokeThickness = 1 };
        path.SetResourceReference(StyleProperty, "Gk.IconPath");
        return path;
    }

    // 既定は 100,100（設定が空のとき）
    private void RestoreLocation()
    {
        var s = App.Settings.RevisionPopUpWindowLocationState;
        var rect = s.Width >= 300 && s.Height >= 200 ? new Rect(s.Left, s.Top, s.Width, s.Height) : new Rect(100, 100, 1000, 600);
        if (!rect.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)))
            rect = new Rect(100, 100, 1000, 600);
        Left = rect.Left; Top = rect.Top; Width = rect.Width; Height = rect.Height;
    }

    private void SaveLocation()
    {
        var s = App.Settings.RevisionPopUpWindowLocationState;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.Left = (int)r.Left;
        s.Top = (int)r.Top;
        s.Width = (int)r.Width;
        s.Height = (int)r.Height;
        s.WindowState = 0;
    }
}
