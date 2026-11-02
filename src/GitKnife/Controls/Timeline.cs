using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// History / Blame の上の時間軸（枠の中にコミットごとの細い縦線、選んだコミットに青い三角と線、
/// 下に月の目盛り "1/2026" … "2/2026"）。押すといちばん近いコミットを選ぶ。
/// </summary>
public sealed class Timeline : FrameworkElement
{
    private IReadOnlyList<DateTimeOffset> _dates = [];
    private int _selected = -1;

    /// <summary>コミットの日時（並びは問わない）と、選んだもの（添字）。</summary>
    public void SetCommits(IReadOnlyList<DateTimeOffset> dates, int selected)
    {
        _dates = dates;
        _selected = selected;
        InvalidateVisual();
    }

    public int Selected
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>押されたコミットの添字。</summary>
    public event EventHandler<int>? CommitClicked;

    public Timeline()
    {
        Height = 42;
        Cursor = Cursors.Hand;
    }

    private (DateTime start, DateTime end) Range()
    {
        var min = _dates.Min().LocalDateTime;
        var max = _dates.Max().LocalDateTime;
        var start = new DateTime(min.Year, min.Month, 1);
        var end = new DateTime(max.Year, max.Month, 1).AddMonths(1);
        return (start, end);
    }

    private double X(DateTime t, DateTime start, DateTime end) => 1 + (ActualWidth - 2) * ((t - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds));

    protected override void OnRender(DrawingContext dc)
    {
        var border = TryFindResource("Gk.Border") as Brush ?? Brushes.DimGray;
        var dim = TryFindResource("Gk.Text.Dim") as Brush ?? Brushes.Gray;
        var text = TryFindResource("Gk.Text") as Brush ?? Brushes.White;
        var accent = TryFindResource("Gk.Accent") as Brush ?? Brushes.DeepSkyBlue;
        var box = new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), 16);
        dc.DrawRectangle(Brushes.Transparent, new Pen(border, 1), box);
        if (_dates.Count == 0) return;
        var (start, end) = Range();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface((FontFamily)FindResource("Gk.UiFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // 月の目盛り（端は左寄せ・右寄せ、間は中央）
        for (var m = start; m <= end; m = m.AddMonths(1))
        {
            var x = X(m, start, end);
            dc.DrawLine(new Pen(border, 1), new Point(x, 17), new Point(x, 21));
            var label = new FormattedText($"{m.Month}/{m.Year}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10.5, text, dpi);
            var lx = m == start ? Math.Max(0, x - label.Width / 2) : m == end ? Math.Min(ActualWidth - label.Width, x - label.Width / 2) : x - label.Width / 2;
            dc.DrawText(label, new Point(lx, 23));
        }
        // コミットの縦線
        var pen = new Pen(dim, 1);
        foreach (var d in _dates)
        {
            var x = Math.Round(X(d.LocalDateTime, start, end)) + 0.5;
            dc.DrawLine(pen, new Point(x, 2), new Point(x, 16));
        }
        // 選んだコミット：青い三角と線
        if (_selected >= 0 && _selected < _dates.Count)
        {
            var x = Math.Round(X(_dates[_selected].LocalDateTime, start, end)) + 0.5;
            dc.DrawLine(new Pen(accent, 1.5), new Point(x, 2), new Point(x, 16));
            var tri = new StreamGeometry();
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(x - 5, 0), true, true);
                g.LineTo(new Point(x + 5, 0), true, false);
                g.LineTo(new Point(x, 6), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(accent, null, tri);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dates.Count == 0) return;
        var (start, end) = Range();
        var px = e.GetPosition(this).X;
        var best = 0;
        for (var i = 1; i < _dates.Count; i++)
            if (Math.Abs(X(_dates[i].LocalDateTime, start, end) - px) < Math.Abs(X(_dates[best].LocalDateTime, start, end) - px)) best = i;
        CommitClicked?.Invoke(this, best);
    }
}
