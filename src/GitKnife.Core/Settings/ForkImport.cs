using System.Text.Json;

namespace GitKnife.Core.Settings;

/// <summary>Fork の settings.json から読んだ、取り込む部分。</summary>
public sealed record ForkImportSource(WorkspacesSettings Workspaces, RepositoryManagerSettings? RepositoryManager, string? RepositoryListPath);

/// <summary>
/// Fork の設定を取り込む（2026-10-28 ユーザーの依頼。Workspaces > Import from Fork...）。全部置き換える：
/// - settings.json の Workspaces（ワークスペースとタブ、ActiveWorkspace、ShowInTitle）と RepositoryManager（前の版の形）
/// - %LocalAppData%\ForkData\repositories.toml（Repository Manager の一覧・Source Code Folder・タブの色）
/// 置き換える前のファイルは "*.before-fork-import" に残す。キーは GitKnife の設定と同じなので、そのまま写せる。
/// Fork は終わるときに settings.json を書くので、動いている Fork の今のタブはまだファイルに無いことがある。
/// </summary>
public static class ForkImport
{
    public const string BackupSuffix = ".before-fork-import";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string DefaultSettingsPath => Path.Combine(LocalAppData, "Fork", "settings.json");
    public static string DefaultRepositoryListPath => Path.Combine(LocalAppData, "ForkData", "repositories.toml");

    /// <summary>Fork の設定を読む。settings.json が無いか Workspaces が無ければ null。</summary>
    public static ForkImportSource? Read(string? settingsPath = null, string? repositoryListPath = null)
    {
        settingsPath ??= DefaultSettingsPath;
        repositoryListPath ??= DefaultRepositoryListPath;
        if (!File.Exists(settingsPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("Workspaces", out var ws)) return null;
        var workspaces = ws.Deserialize<WorkspacesSettings>(SettingsStore.JsonOptions) ?? new WorkspacesSettings();
        var manager = doc.RootElement.TryGetProperty("RepositoryManager", out var rm) ? rm.Deserialize<RepositoryManagerSettings>(SettingsStore.JsonOptions) : null;
        return new ForkImportSource(workspaces, manager, File.Exists(repositoryListPath) ? repositoryListPath : null);
    }

    /// <summary>
    /// target の Workspaces と RepositoryManager を置き換え、repositories.toml を Fork のもので置き換える（Fork に無ければ今のまま）。
    /// settings.json は呼んだ側が書く。settingsPath を渡すと、書く前の settings.json を控えに写す。
    /// </summary>
    public static void Apply(ForkImportSource source, AppSettings target, string? settingsPath = null, string? repositoryListPath = null)
    {
        settingsPath ??= SettingsStore.DefaultPath;
        repositoryListPath ??= RepositoryList.DefaultPath;
        Backup(settingsPath);
        target.Workspaces = source.Workspaces;
        if (source.RepositoryManager is not null) target.RepositoryManager = source.RepositoryManager;
        if (source.RepositoryListPath is null) return;
        Backup(repositoryListPath);
        Directory.CreateDirectory(Path.GetDirectoryName(repositoryListPath)!);
        File.Copy(source.RepositoryListPath, repositoryListPath, overwrite: true);
    }

    private static void Backup(string path)
    {
        if (File.Exists(path)) File.Copy(path, path + BackupSuffix, overwrite: true);
    }
}
