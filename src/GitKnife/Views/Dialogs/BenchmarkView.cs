using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Benchmark の本文：左上に題名（灰色の太字）とサブタイトル、右上に測り直しの ↻、その下の枠に
/// 最初は Start、測った後は 4 行（名前と緑の秒）、"SCORE"（大きな太字）と右に大きな緑の数字と下線、左下に "Copy report" のリンク。
/// </summary>
internal static class BenchmarkView
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));

    public static FrameworkElement Create(BenchmarkDialogViewModel vm)
    {
        var root = new Grid { Height = 312 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid();
        var titles = new StackPanel();
        var title = new TextBlock { Text = vm.Title, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        titles.Children.Add(title);
        titles.Children.Add(new TextBlock { Text = vm.Subtitle, FontSize = 13 });
        head.Children.Add(titles);
        var again = new Button { Content = "↻", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(4, 0, 4, 0), ToolTip = "Run again" };
        again.SetResourceReference(FrameworkElement.StyleProperty, "Gk.FlatButton");
        AutomationProperties.SetAutomationId(again, "RefreshButton");
        again.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(BenchmarkDialogViewModel.IsStarted)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        again.Click += (_, _) => _ = vm.RunAsync();
        head.Children.Add(again);
        root.Children.Add(head);

        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(20, 14, 20, 14) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");
        Grid.SetRow(frame, 1);
        root.Children.Add(frame);
        var body = new Grid();
        frame.Child = body;

        var start = new Button { Content = "Start", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 2, 10, 2) };
        start.SetResourceReference(FrameworkElement.StyleProperty, "Gk.Button");
        AutomationProperties.SetAutomationId(start, "StartButton");
        AutomationProperties.SetName(start, "Start");
        start.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(BenchmarkDialogViewModel.IsStarted)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility"), ConverterParameter = "invert,hidden" });
        start.Click += (_, _) => _ = vm.RunAsync();
        body.Children.Add(start);

        var results = new StackPanel();
        results.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(BenchmarkDialogViewModel.IsStarted)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        foreach (var item in vm.Items)
        {
            var row = new Grid { Height = 34, Background = Brushes.Transparent, ToolTip = item.Tip };
            row.Children.Add(new TextBlock { Text = item.Name, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            var seconds = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Green, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Width = 90 };
            seconds.SetBinding(TextBlock.TextProperty, new Binding(nameof(BenchmarkItem.Seconds)) { Source = item });
            AutomationProperties.SetAutomationId(seconds, item.Id);
            row.Children.Add(seconds);
            results.Children.Add(row);
        }
        var scoreRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        var scoreLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        scoreLeft.Children.Add(new TextBlock { Text = "SCORE", FontSize = 23, FontWeight = FontWeights.Bold });
        // リンクの見た目のボタン
        var copyText = new TextBlock { Text = "Copy report", FontSize = 12 };
        copyText.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Accent");
        var copy = new Button { Content = copyText, Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) } };
        AutomationProperties.SetAutomationId(copy, "CopyReportButton");
        AutomationProperties.SetName(copy, "Copy report");
        copy.Click += (_, _) => { try { Clipboard.SetText(vm.Report); } catch (System.Runtime.InteropServices.COMException) { } };
        scoreLeft.Children.Add(copy);
        scoreRow.Children.Add(scoreLeft);
        var scoreRight = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Width = 92 };
        var score = new TextBlock { FontSize = 38, Foreground = Green, HorizontalAlignment = HorizontalAlignment.Center };
        score.SetBinding(TextBlock.TextProperty, new Binding(nameof(BenchmarkDialogViewModel.Score)));
        AutomationProperties.SetAutomationId(score, "ScoreTextBlock");
        scoreRight.Children.Add(score);
        // 下の緑の線
        var scoreBar = new ProgressBar { Height = 4, Value = 100, Foreground = Green, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 2, 0, 0) };
        AutomationProperties.SetAutomationId(scoreBar, "ScoreProgressBar");
        scoreRight.Children.Add(scoreBar);
        scoreRow.Children.Add(scoreRight);
        results.Children.Add(scoreRow);
        // 測っている間の進み具合（止まっている間は場所を残して隠す）
        var running = new ProgressBar { Height = 3, IsIndeterminate = true, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetAutomationId(running, "ProgressBar");
        running.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(BenchmarkDialogViewModel.IsRunning)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility"), ConverterParameter = "hidden" });
        results.Children.Add(running);
        body.Children.Add(results);
        return root;
    }
}
