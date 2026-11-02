using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>
/// グラフの列の色。ノードは明るい色、線は少し暗い色（ノード #FF9502 に対して線 #C7780A ≒ 0.78 倍）。
/// ラベルの背景は列の色を一覧の背景に 28% で重ねた色、枠は線の色。
/// </summary>
public static class GraphPalette
{
    private static readonly Color[] Nodes =
    [
        Color.FromRgb(0xFF, 0x95, 0x02), // オレンジ
        Color.FromRgb(0xF5, 0xC4, 0x00), // 黄
        Color.FromRgb(0xFF, 0x43, 0x39), // 赤（線 #C7342C から）
        Color.FromRgb(0x3D, 0xBE, 0x6E), // 緑
        Color.FromRgb(0x3A, 0xA0, 0xFF), // 青
        Color.FromRgb(0xB3, 0x6B, 0xFF), // 紫
        Color.FromRgb(0xFF, 0x6F, 0xAE), // ピンク
        Color.FromRgb(0x2E, 0xC4, 0xC4), // 青緑
        Color.FromRgb(0xFF, 0x7A, 0x45), // 朱
        Color.FromRgb(0x7B, 0xD1, 0x48), // 黄緑
        Color.FromRgb(0x6C, 0x8C, 0xFF), // 群青
        Color.FromRgb(0xC0, 0xA0, 0x70), // 砂
    ];

    public static int Count => Nodes.Length;

    private static readonly SolidColorBrush[] NodeBrushes = Nodes.Select(Freeze).ToArray();
    private static readonly SolidColorBrush[] LineBrushes = Nodes.Select(c => Freeze(Scale(c, 0.78))).ToArray();
    private static readonly Pen[] LinePens = LineBrushes.Select(b => { var p = new Pen(b, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }).ToArray();

    public static SolidColorBrush Node(int i) => NodeBrushes[Mod(i)];
    public static SolidColorBrush Line(int i) => LineBrushes[Mod(i)];
    public static Pen LinePen(int i) => LinePens[Mod(i)];

    /// <summary>ラベルの背景（列の色を背景に重ねる）。</summary>
    public static SolidColorBrush LabelBackground(int i, Color background) => Freeze(Blend(Nodes[Mod(i)], background, 0.28));

    // Light のラベル（枠は列の色そのもの、地は列の色を白に 13% 混ぜた色。
    // main #FF9502 / #FFF2DD、黄 #FFCC00 / #FFF9DC、赤 #FF3B30 / #FFE5E4。黄と赤は Dark と少し違う色。ほかの色は Dark と同じ）
    private static readonly Color[] LightNodes = Nodes.Select((c, k) => k switch
    {
        1 => Color.FromRgb(0xFF, 0xCC, 0x00),
        2 => Color.FromRgb(0xFF, 0x3B, 0x30),
        _ => c,
    }).ToArray();

    public static SolidColorBrush LightLabelBorder(int i) => Freeze(LightNodes[Mod(i)]);
    public static SolidColorBrush LightLabelBackground(int i) => Freeze(Blend(LightNodes[Mod(i)], Colors.White, 0.13));

    private static int Mod(int i) => ((i % Nodes.Length) + Nodes.Length) % Nodes.Length;
    private static Color Scale(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    private static Color Blend(Color fg, Color bg, double a) => Color.FromRgb(
        (byte)(fg.R * a + bg.R * (1 - a)), (byte)(fg.G * a + bg.G * (1 - a)), (byte)(fg.B * a + bg.B * (1 - a)));
    private static SolidColorBrush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
