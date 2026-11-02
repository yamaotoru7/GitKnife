using System.Windows;
using GitKnife.Core.Settings;
using Microsoft.Win32;

namespace GitKnife;

/// <summary>テーマの切り替え。Theme：0 Light、1 Dark。FollowSystemTheme ならアプリの配色設定に従う。</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>テーマを替えた後（コードで色を決めて描いているもの、IconImage などが描き直す）。</summary>
    public static event Action? Changed;

    public static void Apply(AppSettings settings)
    {
        var dark = settings.FollowSystemTheme ? SystemPrefersDark() : settings.Theme == 1;
        Apply(dark);
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        dicts[0] = new ResourceDictionary { Source = uri };
        Changed?.Invoke();
    }

    /// <summary>Window > Switch Theme。</summary>
    public static void Toggle(AppSettings settings)
    {
        settings.FollowSystemTheme = false;
        settings.Theme = IsDark ? 0 : 1;
        Apply(settings);
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }
}
