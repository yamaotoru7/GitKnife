using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using GitKnife.ViewModels;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// ファイル一覧の Space で開く大きな diff。
/// メインの窓の 90% の大きさの枠なしの窓で、題名はファイル名。上に閉じるボタン、その下に diff の見出しと本文（表示形式は PopupDiffLayoutMode）、
/// 下に "Space bar: close, Up/Down: move file selection"。Space か Esc で閉じ、↑ / ↓ で元の一覧の選択を動かす（中身は選んだファイルに替わる）。
/// 位置は保存しない（設定にキーが無い）。
/// </summary>
internal sealed class DiffPopupWindow : Window
{
    private readonly ListView _list;

    /// <summary>一覧で Space を押したときに開く。<paramref name="source"/> は Diff / DiffTitle を持つビューモデル。</summary>
    public static void Attach(ListView list)
    {
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None) return;
            if (list.SelectedItem is not FileListItem { IsFolder: false } || list.DataContext is null) return;
            new DiffPopupWindow(list, list.DataContext) { Owner = Window.GetWindow(list) }.Show();
            e.Handled = true;
        };
    }

    private DiffPopupWindow(ListView list, object source)
    {
        _list = list;
        DataContext = source;
        SetBinding(TitleProperty, new Binding("DiffTitle"));
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 26, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = WindowFrame.GlassThickness, CornerRadius = new CornerRadius(0) });
        SetResourceReference(BackgroundProperty, "Gk.Details.Background");
        SetResourceReference(ForegroundProperty, "Gk.Text");
        SetResourceReference(FontFamilyProperty, "Gk.UiFont");
        FontSize = 12.5;

        var root = new DockPanel();
        var bar = new DockPanel { Height = 26, LastChildFill = false };
        DockPanel.SetDock(bar, Dock.Top);
        var closePath = new Path { Data = (Geometry)FindResource("Icon.Close"), StrokeThickness = 1 };
        closePath.SetResourceReference(StyleProperty, "Gk.IconPath");
        var close = new Button { Content = closePath };
        close.SetResourceReference(StyleProperty, "Gk.CloseCaptionButton");
        AutomationProperties.SetAutomationId(close, "PART_CloseButton");
        close.Click += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        root.Children.Add(bar);

        var footer = new TextBlock { Text = "Space bar: close, Up/Down: move file selection", FontSize = 11, Margin = new Thickness(20, 4, 20, 6) };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var panel = new FileDiffPanel { Place = DiffPlace.Popup, ShowActions = false, Margin = new Thickness(20, 0, 20, 0) };
        panel.SetBinding(FileDiffPanel.DiffProperty, new Binding("Diff"));
        panel.SetBinding(FileDiffPanel.TitleProperty, new Binding("DiffTitle"));
        root.Children.Add(panel);
        Content = new Border { Child = root, BorderThickness = new Thickness(0) }.With(b => b.SetResourceReference(Border.BorderBrushProperty, "Gk.Border"));

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => Place();
    }

    /// <summary>メインの窓の 90% の大きさで、その真ん中に置く。</summary>
    private void Place()
    {
        if (Owner is not { } o) { Width = 1000; Height = 700; return; }
        var r = o.WindowState == WindowState.Maximized ? GetOwnerBounds(o) : new Rect(o.Left, o.Top, o.ActualWidth, o.ActualHeight);
        Width = r.Width * 0.9;
        Height = r.Height * 0.9;
        Left = r.Left + (r.Width - Width) / 2;
        Top = r.Top + (r.Height - Height) / 2;
    }

    /// <summary>最大化した窓の画面上の位置（Left / Top は元の大きさのときの値なので、窓の左上を画面の座標に直して求める）。</summary>
    private static Rect GetOwnerBounds(Window o)
    {
        var topLeft = o.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(o);
        return new Rect(topLeft.X / dpi.DpiScaleX, topLeft.Y / dpi.DpiScaleY, o.ActualWidth, o.ActualHeight);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space or Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Up or Key.Down:
                MoveSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>元の一覧で前後のファイル（フォルダの行は飛ばす）を選ぶ。</summary>
    private void MoveSelection(int delta)
    {
        var items = _list.Items;
        var i = items.IndexOf(_list.SelectedItem);
        for (var j = i + delta; j >= 0 && j < items.Count; j += delta)
        {
            if (items[j] is FileListItem { IsFolder: false } next)
            {
                _list.SelectedItem = next;
                _list.ScrollIntoView(next);
                return;
            }
        }
    }
}
