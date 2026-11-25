using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.Core.Git;
using GitKnife.Core.Logging;

namespace GitKnife;

/// <summary>
/// 時間がかかるかもしれない処理（接続先のファイルの読み書きなど）を、UI のスレッドを止めずに待つ。
/// 待つ間は窓への入力を止め（二度押しや、途中の状態での別の操作を防ぐ）、<see cref="ShowAfter"/> を過ぎても終わらなければ
/// 窓に「処理中」の表示（止められる処理なら Cancel 付き）を重ねる。一度出したら <see cref="MinimumVisible"/> は出しておく（すぐ消えるとちらつく）。
/// Cancel（または Esc）で止めると <see cref="OperationCanceledException"/> を投げる。
/// </summary>
public static class Busy
{
    public static readonly TimeSpan ShowAfter = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromSeconds(1);

    /// <summary>同期の処理（<see cref="HostFile"/>、<see cref="HostDirectory"/>、それを使う設定の読み書き）を UI のスレッドの外で動かして待つ。</summary>
    public static Task<T> IoAsync<T>(DependencyObject? anchor, string message, Func<T> work)
        => RunAsync(anchor, message, ct => HostIO.RunAsync(work, ct));

    /// <inheritdoc cref="IoAsync{T}(DependencyObject?, string, Func{T})"/>
    public static Task IoAsync(DependencyObject? anchor, string message, Action work)
        => RunAsync(anchor, message, ct => HostIO.RunAsync(work, ct));

    public static async Task RunAsync(DependencyObject? anchor, string message, Func<CancellationToken, Task> work, bool cancellable = true)
        => await RunAsync(anchor, message, async ct => { await work(ct); return true; }, cancellable);

    /// <summary>work を始めて終わるまで待つ（UI のスレッドから呼ぶ）。anchor は表示を重ねる窓（の中の要素）。null なら今の窓。</summary>
    public static async Task<T> RunAsync<T>(DependencyObject? anchor, string message, Func<CancellationToken, Task<T>> work, bool cancellable = true)
    {
        using var cts = new CancellationTokenSource();
        var task = work(cts.Token);
        if (task.IsCompleted) return await task;

        var overlay = Overlay.Attach(FindWindow(anchor), message, cancellable ? cts.Cancel : null);
        long shownAt = 0;
        try
        {
            if (await Task.WhenAny(task, Task.Delay(ShowAfter, cts.Token)) != task && !cts.IsCancellationRequested)
            {
                overlay?.Reveal();
                shownAt = Stopwatch.GetTimestamp();
            }
            try
            {
                // Cancel を押したら、止まるのを待たずに戻る（止まらない処理も、裏で終わるまで動かしておく）
                return await task.WaitAsync(cts.Token);
            }
            finally
            {
                if (shownAt != 0 && !cts.IsCancellationRequested)
                {
                    var left = MinimumVisible - Stopwatch.GetElapsedTime(shownAt);
                    if (left > TimeSpan.Zero) await Task.Delay(left);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Log.Info($"Canceled: {message}");
            ObserveLater(task);
            throw;
        }
        finally { overlay?.Detach(); }
    }

    /// <summary>止めた後で失敗したタスクの例外を拾っておく（拾わないと UnobservedTaskException になる）。</summary>
    private static void ObserveLater(Task task) => task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);

    private static Window? FindWindow(DependencyObject? anchor)
    {
        if (anchor is Window w) return w;
        if (anchor is not null && Window.GetWindow(anchor) is { } owner) return owner;
        var windows = Application.Current?.Windows.OfType<Window>().ToList() ?? [];
        return windows.FirstOrDefault(x => x.IsActive) ?? Application.Current?.MainWindow;
    }

    /// <summary>窓の中身に重ねる層。始めは透明で入力だけ止め、<see cref="Reveal"/> で暗くして「処理中」を出す。</summary>
    private sealed class Overlay : Adorner
    {
        private readonly Window _window;
        private readonly AdornerLayer _layer;
        private readonly Border _root;
        private readonly Border _card;
        private readonly Action? _cancel;
        private readonly IInputElement? _focused;

        private Overlay(Window window, UIElement content, AdornerLayer layer, string message, Action? cancel) : base(content)
        {
            _window = window;
            _layer = layer;
            _cancel = cancel;
            _focused = Keyboard.FocusedElement;

            var panel = new StackPanel { MinWidth = 220, MaxWidth = 420 };
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 0, 0, 10) });
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text");
            panel.Children.Add(text);
            if (cancel is not null)
            {
                var button = new Button { Content = "Cancel", MinWidth = 80, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                button.SetResourceReference(StyleProperty, "Gk.Button");
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "BusyCancelButton");
                button.Click += (_, _) => _cancel?.Invoke();
                panel.Children.Add(button);
            }
            _card = new Border
            {
                Child = panel,
                Padding = new Thickness(20, 16, 20, 16),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 },
            };
            _card.SetResourceReference(Border.BackgroundProperty, "Gk.Dialog.Background");
            _card.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");
            System.Windows.Automation.AutomationProperties.SetAutomationId(_card, "BusyOverlay");
            // 透明でも Background があれば押した場所に当たる（下の画面に届かない）
            _root = new Border { Background = Brushes.Transparent, Child = _card, Cursor = Cursors.AppStarting };
            AddVisualChild(_root);
            _window.PreviewKeyDown += OnPreviewKeyDown;
        }

        public static Overlay? Attach(Window? window, string message, Action? cancel)
        {
            if (window?.Content is not UIElement content || AdornerLayer.GetAdornerLayer(content) is not { } layer) return null;
            var overlay = new Overlay(window, content, layer, message, cancel);
            layer.Add(overlay);
            return overlay;
        }

        public void Reveal()
        {
            _root.Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
            _root.Cursor = Cursors.Wait;
            _card.Visibility = Visibility.Visible;
        }

        public void Detach()
        {
            _window.PreviewKeyDown -= OnPreviewKeyDown;
            _layer.Remove(this);
            if (_focused is UIElement { IsVisible: true } e) e.Focus();
        }

        /// <summary>待つ間はキーも下の画面に届けない（Esc は Cancel）。</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) _cancel?.Invoke();
            else if (e.OriginalSource is DependencyObject d && IsInside(d) && e.Key is Key.Space or Key.Enter or Key.Tab) return;   // Cancel ボタン
            e.Handled = true;
        }

        private bool IsInside(DependencyObject d)
        {
            for (var x = d; x is not null; x = VisualTreeHelper.GetParent(x)) if (ReferenceEquals(x, _root)) return true;
            return false;
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _root;

        protected override Size MeasureOverride(Size constraint)
        {
            var size = AdornedElement.RenderSize;
            _root.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _root.Arrange(new Rect(AdornedElement.RenderSize));
            return finalSize;
        }
    }
}
