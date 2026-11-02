using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GitKnife.Controls;

/// <summary>
/// ファイルの種類の小さなアイコン（Windows がその拡張子に出すアイコン。SHGetFileInfo + SHGFI_USEFILEATTRIBUTES なのでファイルが無くても取れる）。
/// 拡張子ごとに覚える。
/// </summary>
public sealed class ShellIconConverter : IValueConverter
{
    public static readonly ShellIconConverter Instance = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path && path.Length > 0 ? For(path) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public static ImageSource? For(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        var key = ext.Length > 0 ? ext : "<none>";
        return Cache.GetOrAdd(key, _ => Load(ext.Length > 0 ? "x" + ext : "x"));
    }

    /// <summary>実在するファイルそのもののアイコン（exe なら埋め込みのアイコン。Preferences の外部ツールの一覧）。無ければ null。</summary>
    public static ImageSource? ForFile(string path)
        => path.Length > 0 && System.IO.File.Exists(path) ? Cache.GetOrAdd("file:" + path, _ => Load(path, useAttributes: false)) : null;

    /// <summary>フォルダのアイコン（エクスプローラーの黄色いフォルダ。Preferences の Source Code Folder）。</summary>
    public static ImageSource? Folder => Cache.GetOrAdd("<folder>", _ => Load("folder", attributes: FILE_ATTRIBUTE_DIRECTORY));

    private static ImageSource? Load(string name, bool useAttributes = true, uint attributes = FILE_ATTRIBUTE_NORMAL)
    {
        var info = new SHFILEINFO();
        var r = SHGetFileInfo(name, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0));
        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
