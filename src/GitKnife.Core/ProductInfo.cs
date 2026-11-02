using System.Reflection;

namespace GitKnife.Core;

/// <summary>製品名やファイルの置き場所など、アプリ全体で共有する定数。</summary>
public static class ProductInfo
{
    public const string Name = "GitKnife";

    /// <summary>csproj の &lt;Version&gt;（Directory.Build.props）。</summary>
    public static string Version { get; } =
        typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(ProductInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>%LocalAppData%\GitKnife。settings.json とログの置き場。</summary>
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    /// <summary>リポジトリ別設定のディレクトリ名（.git/gitknife/）。</summary>
    public const string RepositoryConfigDirectoryName = "gitknife";
}
