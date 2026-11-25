using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.ViewModels;

namespace GitKnife.Controls;

/// <summary>
/// 画像の diff。古い側（SrcFileContentUserControl）と新しい側（DstFileContentUserControl）を左右に並べ、
/// それぞれに題名（created / deleted / old / new）、灰色の枠に入れた画像、"W: 64px | H: 48px (192 バイト)"（大きさの単位は Windows の書き方）を出す。
/// 中身は diff の index 行の blob（`git cat-file blob`）から。作業ツリーの側で blob が無ければファイルを読む。
/// 両側があるときは下に Side-by-Side / Swipe / Onion Skin の切り替え（SideBySideRadioButton / SwipeRadioButton / OnionSkinRadioButton。選んだ表示は保存しない）。
/// Swipe は境目（GridSplitter）の左が古い方、右が新しい方。Onion Skin は Slider で新しい方の濃さを変える。説明の横の ⌄ のメニューは未実装。
/// 画像でないバイナリは、画像の代わりに灰色のカード（109×130、ファイルのアイコンと ".dat"）と "10 バイト (10 B)"。切り替えは出さない。
/// </summary>
internal sealed class ImageDiffView : UserControl
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff" };

    private readonly Pane _src = new("SrcFileContentUserControl");
    private readonly Pane _dst = new("DstFileContentUserControl");
    private readonly Grid _sideBySide = new();
    private readonly OverlayView _swipe = new(onion: false) { Visibility = Visibility.Collapsed };
    private readonly OverlayView _onion = new(onion: true) { Visibility = Visibility.Collapsed };
    private readonly StackPanel _modes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6) };
    private readonly RadioButton _sideBySideRadio;
    private int _loadVersion;

    public static bool IsImage(FileDiff? diff) => diff is { IsBinary: true } && Extensions.Contains(System.IO.Path.GetExtension(diff.Path));
    public static bool IsBinary(FileDiff? diff) => diff is { IsBinary: true };

    public ImageDiffView()
    {
        _sideBySide.ColumnDefinitions.Add(new ColumnDefinition());
        _sideBySide.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_dst, 1);
        _sideBySide.Children.Add(_src);
        _sideBySide.Children.Add(_dst);

        RadioButton Mode(string id, string text, UIElement view)
        {
            var r = new RadioButton { Content = text, GroupName = "ImageDiffMode", Margin = new Thickness(0, 0, 18, 0) };
            r.SetResourceReference(ForegroundProperty, "Gk.Text");
            AutomationProperties.SetAutomationId(r, id);
            r.Checked += (_, _) =>
            {
                _sideBySide.Visibility = view == _sideBySide ? Visibility.Visible : Visibility.Collapsed;
                _swipe.Visibility = view == _swipe ? Visibility.Visible : Visibility.Collapsed;
                _onion.Visibility = view == _onion ? Visibility.Visible : Visibility.Collapsed;
            };
            _modes.Children.Add(r);
            return r;
        }
        _sideBySideRadio = Mode("SideBySideRadioButton", "Side-by-Side", _sideBySide);
        Mode("SwipeRadioButton", "Swipe", _swipe);
        Mode("OnionSkinRadioButton", "Onion Skin", _onion);
        _sideBySideRadio.IsChecked = true;

        var root = new DockPanel();
        DockPanel.SetDock(_modes, Dock.Bottom);
        root.Children.Add(_modes);
        var views = new Grid();
        views.Children.Add(_sideBySide);
        views.Children.Add(_swipe);
        views.Children.Add(_onion);
        root.Children.Add(views);
        Content = root;
    }

    public async Task LoadAsync(RepositoryViewModel repo, FileDiff diff)
    {
        var version = ++_loadVersion;
        var image = IsImage(diff);
        if (!image)
        {
            // 画像でないファイルはカードに大きさを出すだけなので、中身は取らない（SSH の先の大きなファイルを丸ごと読んで UI が止まった）
            var oldSize = diff.IsNew ? null : await ReadBlobSizeAsync(repo, diff.OldIndex);
            var newSize = diff.IsDeleted ? null : await ReadBlobSizeAsync(repo, diff.NewIndex) ?? await Task.Run(() => ReadWorkingFileSize(repo, diff.NewPath));
            if (version != _loadVersion) return;
            var bothSides = oldSize is not null && newSize is not null;
            var extension = System.IO.Path.GetExtension(diff.Path);
            _src.ShowFile(oldSize, bothSides ? "old" : "deleted", "Gk.Change.Deleted", extension);
            _dst.ShowFile(newSize, bothSides ? "new" : "created", "Gk.Change.Added", extension);
            ShowSides(oldSize is not null, newSize is not null, modes: false);
            return;
        }
        var oldBytes = diff.IsNew ? null : await ReadBlobAsync(repo, diff.OldIndex);
        var newBytes = diff.IsDeleted ? null : await ReadBlobAsync(repo, diff.NewIndex) ?? await Task.Run(() => ReadWorkingFile(repo, diff.NewPath));
        if (version != _loadVersion) return;
        var both = oldBytes is not null && newBytes is not null;
        _src.Show(oldBytes, both ? "old" : "deleted", "Gk.Change.Deleted");
        _dst.Show(newBytes, both ? "new" : "created", "Gk.Change.Added");
        ShowSides(oldBytes is not null, newBytes is not null, modes: both);
        if (both)
        {
            _swipe.Show(Decode(oldBytes!), Decode(newBytes!));
            _onion.Show(Decode(oldBytes!), Decode(newBytes!));
        }
    }

    /// <summary>片側だけのときは、その側を真ん中に。切り替え（Side-by-Side / Swipe / Onion Skin）は両側の画像があるときだけ。</summary>
    private void ShowSides(bool hasOld, bool hasNew, bool modes)
    {
        _sideBySide.ColumnDefinitions[0].Width = hasOld ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _sideBySide.ColumnDefinitions[1].Width = hasNew ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _modes.Visibility = modes ? Visibility.Visible : Visibility.Collapsed;
        _sideBySideRadio.IsChecked = true;
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Swipe（SwipeImageDiffView）と Onion Skin（OnionSkinImageDiffView）：上に "old" と "new"、その下に 2 つの画像を重ねる。
    /// Swipe は境目のつまみ（GridSplitter）を左右に動かし、左に古い方、右に新しい方を出す。Onion Skin は Slider で新しい方の濃さを変える。
    /// </summary>
    private sealed class OverlayView : UserControl
    {
        private readonly bool _onion;
        private readonly Image _old = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly Image _new = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly Grid _stack = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly System.Windows.Shapes.Rectangle _line = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly System.Windows.Controls.Primitives.Thumb _splitter = new() { Width = 6, Height = 18, Cursor = System.Windows.Input.Cursors.SizeWE };
        private readonly Slider _slider = new() { Width = 220, Minimum = 0, Maximum = 1, Value = 1 };
        private double _split = 0.5;

        public OverlayView(bool onion)
        {
            _onion = onion;
            AutomationProperties.SetAutomationId(this, onion ? "OnionSkinImageDiffView" : "SwipeImageDiffView");
            var labels = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
            TextBlock Label(string text, string id, string color)
            {
                var t = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                t.SetResourceReference(TextBlock.ForegroundProperty, color);
                AutomationProperties.SetAutomationId(t, id);
                return t;
            }
            labels.Children.Add(Label("old", "OldTextBlock", "Gk.Change.Deleted"));
            // Onion Skin は "old" と "new" を離して置き、Slider は画像の下
            if (onion) labels.Children.Add(new Border { Width = 120 });
            labels.Children.Add(Label("new", "NewTextBlock", "Gk.Change.Added"));

            // 画像のない所は市松模様、外側に灰色の枠
            _stack.Background = Checkerboard();
            _stack.Children.Add(_old);
            _stack.Children.Add(_new);
            if (!onion)
            {
                _line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Gk.Accent");
                _stack.Children.Add(_line);
            }
            var frame = new Border { Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Child = _stack };
            frame.SetResourceReference(Border.BackgroundProperty, "Gk.ImageFrame");
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(labels);
            if (!onion)
            {
                AutomationProperties.SetAutomationId(_splitter, "GridSplitter");
                _splitter.SetResourceReference(BackgroundProperty, "Gk.Text.Dim");
                _splitter.DragDelta += (_, e) =>
                {
                    var w = Math.Max(1, _stack.ActualWidth);
                    _split = Math.Clamp(_split + e.HorizontalChange / w, 0, 1);
                    UpdateClip();
                };
                var track = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                track.Children.Add(_splitter);
                panel.Children.Add(track);
                _stack.SizeChanged += (_, _) => UpdateClip();
            }
            panel.Children.Add(frame);
            if (onion)
            {
                AutomationProperties.SetAutomationId(_slider, "Slider");
                _slider.Margin = new Thickness(0, 8, 0, 0);
                _slider.ValueChanged += (_, _) => _new.Opacity = _slider.Value;
                panel.Children.Add(_slider);
            }
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        public void Show(BitmapSource? oldImage, BitmapSource? newImage)
        {
            _old.Source = oldImage;
            _new.Source = newImage;
            _split = 0.5;
            _slider.Value = 1;
            _new.Opacity = _onion ? _slider.Value : 1;
            UpdateClip();
        }

        /// <summary>Swipe：新しい方は境目より右だけ見せ、つまみを境目の位置へ。</summary>
        private void UpdateClip()
        {
            if (_onion) return;
            var w = _stack.ActualWidth;
            var h = _stack.ActualHeight;
            if (w <= 0) return;
            var x = w * _split;
            _new.Clip = new RectangleGeometry(new Rect(x, 0, Math.Max(0, w - x), h));
            _line.Margin = new Thickness(Math.Max(0, x - 0.5), 0, 0, 0);
            _splitter.RenderTransform = new TranslateTransform(x - w / 2, 0);
        }

        /// <summary>透明な所と大きさの違う所を見せる市松模様（8px）。</summary>
        private static Brush Checkerboard()
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            var dark = new GeometryGroup();
            dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
            dark.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), null, dark));
            var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
            b.Freeze();
            return b;
        }
    }

    private static async Task<byte[]?> ReadBlobAsync(RepositoryViewModel repo, string? sha)
    {
        if (string.IsNullOrEmpty(sha) || sha.All(c => c == '0')) return null;
        var r = await repo.Git.RunAsync(new GitRequest(repo.Path, ["cat-file", "blob", sha]) { BinaryOutput = true });
        return r.Succeeded ? r.BinaryStandardOutput : null;
    }

    /// <summary>blob の大きさ（`cat-file -s`）。</summary>
    private static async Task<long?> ReadBlobSizeAsync(RepositoryViewModel repo, string? sha)
    {
        if (string.IsNullOrEmpty(sha) || sha.All(c => c == '0')) return null;
        var r = await repo.Git.RunAsync(new GitRequest(repo.Path, ["cat-file", "-s", sha]));
        return r.Succeeded && long.TryParse(r.StandardOutput.Trim(), out var size) ? size : null;
    }

    private static byte[]? ReadWorkingFile(RepositoryViewModel repo, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var full = System.IO.Path.Combine(repo.Path, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try { return Core.Git.HostFile.Exists(full) ? Core.Git.HostFile.ReadAllBytes(full) : null; }
        catch (IOException) { return null; }
    }

    private static long? ReadWorkingFileSize(RepositoryViewModel repo, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var full = System.IO.Path.Combine(repo.Path, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try { return Core.Git.HostFile.GetLength(full); }
        catch (IOException) { return null; }
    }

    /// <summary>片側：題名、枠つきの画像、大きさ。</summary>
    private sealed class Pane : UserControl
    {
        private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
        private readonly Image _image = new() { Stretch = Stretch.None };
        private readonly Border _frame = new() { Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _description = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBlock _extension = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.White };
        private readonly Border _card;

        public Pane(string id)
        {
            AutomationProperties.SetAutomationId(this, id);
            AutomationProperties.SetAutomationId(_title, "TitleTextBlock");
            AutomationProperties.SetAutomationId(_image, "Image");
            AutomationProperties.SetAutomationId(_description, "DescriprionTextBlock");
            _frame.SetResourceReference(Border.BackgroundProperty, "Gk.ImageFrame");
            _description.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text");
            _frame.Child = _image;
            // 画像でないファイルのカード（白いファイルのアイコンと拡張子）
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L13,0 L20,7 L20,26 L0,26 Z"), Fill = Brushes.White, Width = 20, Height = 26, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var cardContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            cardContent.Children.Add(icon);
            cardContent.Children.Add(_extension);
            _card = new Border { Width = 109, Height = 130, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), Child = cardContent, Visibility = Visibility.Collapsed };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(_title);
            panel.Children.Add(_frame);
            panel.Children.Add(_card);
            panel.Children.Add(_description);
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        /// <summary>画像でないファイルのカード（拡張子と大きさ）。size が null ならこの側を隠す。</summary>
        public void ShowFile(long? size, string title, string colorKey, string extension)
        {
            Visibility = size is null ? Visibility.Collapsed : Visibility.Visible;
            if (size is not { } length) return;
            _title.Text = title;
            _title.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            _frame.Visibility = Visibility.Collapsed;
            _card.Visibility = Visibility.Visible;
            _image.Source = null;
            _extension.Text = extension;
            _description.Text = $"{FormatSize(length)} ({length:N0} B)";
        }

        /// <summary>画像。</summary>
        public void Show(byte[]? bytes, string title, string colorKey)
        {
            Visibility = bytes is null ? Visibility.Collapsed : Visibility.Visible;
            if (bytes is null) return;
            _title.Text = title;
            _title.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            _frame.Visibility = Visibility.Visible;
            _card.Visibility = Visibility.Collapsed;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                _image.Source = bmp;
                // 大きな画像は枠に収める（縦横比はそのまま）
                _image.Stretch = Stretch.Uniform;
                _image.StretchDirection = StretchDirection.DownOnly;
                _description.Text = $"W: {bmp.PixelWidth}px | H: {bmp.PixelHeight}px ({FormatSize(bytes.Length)})";
            }
            catch (Exception)
            {
                _image.Source = null;
                _description.Text = $"({FormatSize(bytes.Length)})";
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr StrFormatByteSizeW(long size, StringBuilder buffer, int bufferSize);

        /// <summary>Windows の書き方の大きさ（日本語の Windows なら "192 バイト"）。</summary>
        private static string FormatSize(long size)
        {
            var sb = new StringBuilder(64);
            try { StrFormatByteSizeW(size, sb, sb.Capacity); return sb.ToString(); }
            catch (Exception) { return $"{size} bytes"; }
        }
    }
}
