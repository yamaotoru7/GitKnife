using System.Windows;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// コミット一覧の右端の一致の印（Ctrl+F。#B07600、幅 4px、一覧の右端から 14px 内側）。
/// 一覧が窓に収まるときは行の位置と高さ。収まらないときは一覧全体に対する割合の位置（スクロールバーの印と同じ。推定）。
/// </summary>
public sealed class MatchMarkers : FrameworkElement
{
    private static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x76, 0x00)));
    private IReadOnlyList<int> _indices = [];
    private int _rowCount;
    private double _offset;
    private double _viewport;

    public MatchMarkers() => IsHitTestVisible = false;

    public double RowHeight { get; set; } = 23;
    /// <summary>一覧の上の余白（ListBox の Padding）。</summary>
    public double TopPadding { get; set; } = 2;

    /// <param name="indices">一致した行</param>
    /// <param name="rowCount">一覧の行数</param>
    /// <param name="offset">一番上に見えている行（仮想化した一覧のスクロール位置は行の単位）</param>
    /// <param name="viewport">見えている行の数</param>
    public void Update(IReadOnlyList<int> indices, int rowCount, double offset, double viewport)
    {
        _indices = indices;
        _rowCount = rowCount;
        _offset = offset;
        _viewport = viewport;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_indices.Count == 0 || _rowCount == 0) return;
        var x = ActualWidth - 17;
        var fits = _rowCount <= _viewport;
        var height = fits ? RowHeight : Math.Max(3, ActualHeight / _rowCount);
        foreach (var i in _indices)
        {
            var y = fits ? TopPadding + (i - _offset) * RowHeight : ActualHeight * i / _rowCount;
            dc.DrawRectangle(Fill, null, new Rect(x, y, 4, height));
        }
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
