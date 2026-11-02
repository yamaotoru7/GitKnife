using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// 線で描くアイコン（Icons.xaml の Geometry）を Image として出す。Path は UIA に出ないが、Image なら "Image#StatusImage" などで見える。
/// </summary>
public sealed class IconImage : Image
{
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(nameof(Geometry), typeof(Geometry), typeof(IconImage),
        new PropertyMetadata(null, (d, _) => ((IconImage)d).Update()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(IconImage),
        new PropertyMetadata(null, (d, _) => ((IconImage)d).Update()));

    public Geometry? Geometry { get => (Geometry?)GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    /// <summary>線の色。null なら Gk.Icon（文字より少し薄い灰色）。</summary>
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    // 既定の大きさは 12。コンストラクターで Width = 12 と入れるとローカル値になり、テンプレートの中で付けた Width（サイドバーの 20 など）より
    // 強くなって効かなかったので、既定値として持つ
    static IconImage()
    {
        WidthProperty.OverrideMetadata(typeof(IconImage), new FrameworkPropertyMetadata(12.0));
        HeightProperty.OverrideMetadata(typeof(IconImage), new FrameworkPropertyMetadata(12.0));
        StretchProperty.OverrideMetadata(typeof(IconImage), new FrameworkPropertyMetadata(Stretch.Uniform));
    }

    public IconImage()
    {
        // 線の色はテーマのブラシを描いた時点で使うので、テーマを替えたら描き直す（Window > Switch Theme で Light にしても白いままだった）
        Loaded += (_, _) => { Update(); ThemeManager.Changed += Update; };
        Unloaded += (_, _) => ThemeManager.Changed -= Update;
    }

    private void Update()
    {
        if (Geometry is null) { Source = null; return; }
        var brush = Stroke ?? TryFindResource("Gk.Icon") as Brush ?? TryFindResource("Gk.Text") as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 1.25) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        // 16×16 の座標系に合わせる（アイコンは 16×16 で描いてある）
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(null, pen, Geometry));
        Source = new DrawingImage(group);
    }
}
