using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using GitKnife.Controls;
using GitKnife.Core.Settings;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// History / Blame などの別窓（メインと別のトップレベル窓。題名 "app.cs - History"）の共通部分：
/// カスタムクローム（PART_MinimizeButton / PART_RestoreButton / PART_MaximizeButton / PART_CloseButton）、
/// 見出し（"History" + FileIcon + FileNameTextBlock）、位置の保存（HistoryWindowLocationState など）。
/// </summary>
internal abstract class ToolWindow : Window
{
    private readonly WindowLocationState _location;
    private readonly Button _restore;
    private readonly Button _maximize;
    /// <summary>見出しの右側（Blame のリビジョン選択など）。</summary>
    protected readonly DockPanel HeaderRight = new() { LastChildFill = false };
    private readonly ContentControl _body = new();
    /// <summary>見出しの下の時間軸（History / Blame）。</summary>
    protected readonly Timeline Timeline = new() { Margin = new Thickness(20, 0, 20, 6) };

    protected ToolWindow(string heading, string path, WindowLocationState location, double width, double height)
    {
        _location = location;
        Title = $"{System.IO.Path.GetFileName(path)} - {heading}";
        Width = width;
        Height = height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 26, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
        SetResourceReference(BackgroundProperty, "Gk.ToolWindow.Background");
        SetResourceReference(ForegroundProperty, "Gk.Text");
        SetResourceReference(FontFamilyProperty, "Gk.UiFont");
        FontSize = 12.5;

        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        WindowChrome.SetIsHitTestVisibleInChrome(caption, true);
        caption.Children.Add(CaptionButton("PART_MinimizeButton", "Icon.Minimize", "Gk.CaptionButton", () => WindowState = WindowState.Minimized));
        _restore = CaptionButton("PART_RestoreButton", "Icon.Restore", "Gk.CaptionButton", () => WindowState = WindowState.Normal);
        _maximize = CaptionButton("PART_MaximizeButton", "Icon.Maximize", "Gk.CaptionButton", () => WindowState = WindowState.Maximized);
        caption.Children.Add(_restore);
        caption.Children.Add(_maximize);
        caption.Children.Add(CaptionButton("PART_CloseButton", "Icon.Close", "Gk.CloseCaptionButton", Close));
        root.Children.Add(caption);

        // 見出し
        var header = new DockPanel { Margin = new Thickness(24, 0, 20, 6) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        // ファイルの種類の OS のアイコン（app.cs は .cs のアイコン、フォルダの src は拡張子の無いファイルのアイコン）
        var icon = new Image { Source = ShellIconConverter.For(path), Width = 14, Height = 14, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(icon, "FileIcon");
        title.Children.Add(icon);
        var name = new TextBlock { Text = path, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(name, "FileNameTextBlock");
        title.Children.Add(name);
        DockPanel.SetDock(HeaderRight, Dock.Right);
        header.Children.Add(HeaderRight);
        header.Children.Add(title);
        Grid.SetRow(header, 1);
        root.Children.Add(header);

        Grid.SetRow(Timeline, 2);
        root.Children.Add(Timeline);
        Grid.SetRow(_body, 3);
        root.Children.Add(_body);
        Content = new Border { Child = root, BorderThickness = new Thickness(1) }.With(b => b.SetResourceReference(Border.BorderBrushProperty, "Gk.Border"));

        SourceInitialized += (_, _) => RestoreLocation();
        StateChanged += (_, _) => UpdateButtons();
        Closing += (_, _) => SaveLocation();
        UpdateButtons();
    }

    /// <summary>見出しの下の本文。</summary>
    protected object Body { set => _body.Content = value; }

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
        // 最大化すると枠の外に出る分を内側に寄せる
        BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void RestoreLocation()
    {
        var s = _location;
        if (s.Width < 300 || s.Height < 200) return;
        var rect = new Rect(s.Left, s.Top, s.Width, s.Height);
        if (!rect.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = s.Left; Top = s.Top; Width = s.Width; Height = s.Height;
        if (s.WindowState == (int)WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveLocation()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _location.Left = (int)r.Left;
        _location.Top = (int)r.Top;
        _location.Width = (int)r.Width;
        _location.Height = (int)r.Height;
        _location.WindowState = WindowState == WindowState.Maximized ? (int)WindowState.Maximized : 0;
    }
}
