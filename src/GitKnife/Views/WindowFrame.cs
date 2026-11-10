using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GitKnife.Views;

/// <summary>
/// 自前のタイトルバー（WindowChrome）の窓に、Windows の細い枠・角の丸め・影を付ける。
/// GlassFrameThickness が 0 だと WindowChrome が窓に四角い領域（SetWindowRgn）を付け、Windows が枠も影も描かない。
/// </summary>
public static class WindowFrame
{
    /// <summary>WindowChrome の GlassFrameThickness に使う（下だけ 1。0 以外なら領域を付けず、枠は Windows が描く）。</summary>
    public static readonly Thickness GlassThickness = new(0, 0, 0, 1);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// WindowStyle=None の窓（タイトルバーも枠も持たない）は Windows 11 が既定で角を丸めず枠も描かないので、丸めを頼む。
    /// Windows 10 では効かない（何もしない）。
    /// </summary>
    public static void UseRoundCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var pref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        };
    }
}
