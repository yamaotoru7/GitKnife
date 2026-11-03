using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GitKnife.Core.Logging;
using GitKnife.Core.Reporting;

namespace GitKnife;

/// <summary>
/// Ctrl+Shift+F12：手前の窓（ダイアログも）とメインウィンドウを撮って、バグレポートのダイアログを出す（2026-10-28 ユーザーの依頼）。
/// どの窓でも効くように Window のクラスハンドラで受ける。右クリックメニューは別の Popup でキーが窓まで上がらないので、ContextMenu でも受ける（#10）。
/// 撮るのは画面の実際の画素（重なった窓やツールチップ、開いたメニューもそのまま写る）。
/// </summary>
internal static class BugReporter
{
    private static bool _open;

    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 日本語 IME が有効だと ImeProcessed で届く（MainWindow の Ctrl+0 と同じ）
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        if (key != Key.F12 || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) || _open) return;
        e.Handled = true;
        // 右クリックメニューからは、メニューを出した要素の窓を撮る（メニューは撮ってから閉じる）
        if (sender is System.Windows.Controls.ContextMenu menu)
        {
            var owner = menu.PlacementTarget is { } target ? Window.GetWindow(target) : Application.Current.MainWindow;
            var images = CaptureAll(owner);
            menu.IsOpen = false;
            Show(owner, images);
        }
        else Show(sender as Window, null);
    }

    private static void Show(Window? active, List<BugReportImage>? images)
    {
        _open = true;
        try
        {
            // ダイアログを出す前に撮る
            images ??= CaptureAll(active);
            var vm = new ViewModels.Dialogs.BugReportDialogViewModel(images, active?.Title ?? "");
            Views.Dialogs.DialogWindow.Run(vm, out _);
        }
        finally { _open = false; }
    }

    /// <summary>手前の窓とメインウィンドウを撮る。</summary>
    private static List<BugReportImage> CaptureAll(Window? active)
    {
        var windows = new List<Window>();
        if (active is not null) windows.Add(active);
        if (Application.Current.MainWindow is { } main && main != active) windows.Add(main);
        var images = new List<BugReportImage>();
        foreach (var w in windows)
        {
            try
            {
                if (Capture(w) is { } png)
                    images.Add(new BugReportImage(w == Application.Current.MainWindow ? "main.png" : $"window-{images.Count + 1}.png", png));
            }
            catch (Exception ex) { Log.Warning($"Capturing '{w.Title}' failed: {ex.Message}"); }
        }
        Log.Info($"Bug report: captured {images.Count} window(s)");
        return images;
    }

    // ------------------------------------------------------------------ キャプチャ

    /// <summary>窓の見えている範囲（影を除く。DWM の枠）を画面から写して PNG にする。最小化していれば null。</summary>
    private static byte[]? Capture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || window.WindowState == WindowState.Minimized || !window.IsVisible) return null;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return null;

        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, SRCCOPY | CAPTUREBLT);
            SelectObject(mem, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
