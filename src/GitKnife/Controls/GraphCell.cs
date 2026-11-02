using System.Windows;
using System.Windows.Media;
using GitKnife.Core.Graph;

namespace GitKnife.Controls;

/// <summary>
/// コミット一覧の 1 行ぶんのグラフを描く。行の高さいっぱいに、上半分・下半分の線分とノードを描く。
/// 斜めの線は縦方向の制御点を持つベジェ曲線（なめらかに曲がる）。
/// </summary>
public sealed class GraphCell : FrameworkElement
{
    public const double LaneWidth = 12;
    public const double LeftPadding = 10;
    public const double NodeRadius = 3.5;
    /// <summary>マージのノード（輪の中に山形。開いていると "⌄"、たたむと "›"。直径 12px くらい）。</summary>
    public const double MergeNodeRadius = 5.5;

    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(GraphRow), typeof(GraphCell), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsHeadProperty = DependencyProperty.Register(
        nameof(IsHead), typeof(bool), typeof(GraphCell), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public GraphRow? Row { get => (GraphRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public bool IsHead { get => (bool)GetValue(IsHeadProperty); set => SetValue(IsHeadProperty, value); }

    public static double X(int lane) => LeftPadding + lane * LaneWidth;

    protected override void OnRender(DrawingContext dc)
    {
        var row = Row;
        if (row is null) return;
        var h = ActualHeight;
        var mid = h / 2;
        foreach (var s in row.Segments)
        {
            var pen = GraphPalette.LinePen(s.Color);
            double y1 = s.TopHalf ? 0 : mid, y2 = s.TopHalf ? mid : h;
            double x1 = X(s.From), x2 = X(s.To);
            if (s.From == s.To) { dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2)); continue; }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x1, y1), false, false);
                var cy = (y1 + y2) / 2;
                ctx.BezierTo(new Point(x1, cy), new Point(x2, cy), new Point(x2, y2), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        var center = new Point(X(row.NodeLane), mid);
        var brush = GraphPalette.Node(row.NodeColor);
        if (row.IsMerge)
        {
            // 線が輪の中に見えないように一覧の地の色で塗ってから輪と山形を描く（選んだ行でも中は暗い）
            dc.DrawEllipse(TryFindResource("Gk.List.Background") as Brush, new Pen(brush, 1.5), center, MergeNodeRadius, MergeNodeRadius);
            var chevron = new StreamGeometry();
            using (var ctx = chevron.Open())
            {
                if (row.IsCollapsedMerge)
                {
                    ctx.BeginFigure(new Point(center.X - 1.2, center.Y - 2.8), false, false);
                    ctx.LineTo(new Point(center.X + 1.6, center.Y), true, true);
                    ctx.LineTo(new Point(center.X - 1.2, center.Y + 2.8), true, true);
                }
                else
                {
                    ctx.BeginFigure(new Point(center.X - 2.8, center.Y - 1.2), false, false);
                    ctx.LineTo(new Point(center.X, center.Y + 1.6), true, true);
                    ctx.LineTo(new Point(center.X + 2.8, center.Y - 1.2), true, true);
                }
            }
            chevron.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, chevron);
        }
        else dc.DrawEllipse(brush, null, center, NodeRadius, NodeRadius);
        // HEAD のノードに輪は付けない（普通の丸。HEAD の印は件名の左の青い点）
    }
}
