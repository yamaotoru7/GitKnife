namespace GitKnife.Core.Settings;

/// <summary>
/// リポジトリ共通の設定 `.git/gitknife/config.toml`。
/// worktree 間で共有される。
/// </summary>
public sealed class RepositoryConfig
{
    public const string FileName = "config.toml";

    private readonly TomlDocument _doc;
    private readonly string _path;

    private RepositoryConfig(TomlDocument doc, string path) { _doc = doc; _path = path; }

    public static RepositoryConfig Load(string commonGitDirectory)
    {
        var path = Path.Combine(commonGitDirectory, ProductInfo.RepositoryConfigDirectoryName, FileName);
        return new RepositoryConfig(TomlDocument.Load(path), path);
    }

    public void Save() => _doc.Save(_path);

    public IReadOnlyList<string> PinnedReferences { get => _doc.GetStringArray("pinned_references"); set => _doc.Set("pinned_references", value); }
    public bool SignOff { get => _doc.GetBool("sign_off", false); set => _doc.Set("sign_off", value); }
    public int TabWidth { get => (int)_doc.GetInt("tab_width", 4); set => _doc.Set("tab_width", (long)value); }
    public bool ShowBugtrackerLinks { get => _doc.GetBool("show_bugtracker_links", true); set => _doc.Set("show_bugtracker_links", value); }
    public bool TrustSharedCommands { get => _doc.GetBool("trust_shared_commands", false); set => _doc.Set("trust_shared_commands", value); }
    /// <summary>
    /// Repository Settings > General の Lean Branching の "Main Branch:"（選ぶと trust_shared_commands の後ろに
    /// `lean_branching_main_branch = "feature/login"` を足す）。既定（default (main)）なら null でキーを消す。
    /// </summary>
    public string? LeanBranchingMainBranch
    {
        get => _doc.GetString("lean_branching_main_branch");
        set { if (string.IsNullOrEmpty(value)) _doc.Remove("lean_branching_main_branch"); else _doc.SetAfter("lean_branching_main_branch", value, "trust_shared_commands"); }
    }
    public bool LeanBranchingNoFastForward { get => _doc.GetBool("lean_branching_no_fast_forward", false); set => _doc.Set("lean_branching_no_fast_forward", value); }
    public bool GitignoreSuggestionDismissed { get => _doc.GetBool("gitignore_suggestion_dismissed", false); set => _doc.Set("gitignore_suggestion_dismissed", value); }
    public IReadOnlyList<string> PullRequestsRecentSearchQueries { get => _doc.GetStringArray("pull_requests_recent_search_queries"); set => _doc.Set("pull_requests_recent_search_queries", value); }
    public IReadOnlyList<string> IssuesRecentSearchQueries { get => _doc.GetStringArray("issues_recent_search_queries"); set => _doc.Set("issues_recent_search_queries", value); }
    /// <summary>Search タブの最近の検索（新しい順。`[[recent_revision_search_queries]]` の type / scope / search_string）。</summary>
    public IReadOnlyList<Git.RevisionSearchQuery> RecentRevisionSearchQueries
    {
        get => _doc.GetTables("recent_revision_search_queries")
            .Select(t => new Git.RevisionSearchQuery(Git.RevisionSearchQuery.ParseType(t.GetString("type")), Git.RevisionSearchQuery.ParseScope(t.GetString("scope")), t.GetString("search_string") ?? ""))
            .ToList();
        set => _doc.Set("recent_revision_search_queries", value.Select(q => new TomlTable()
            .Set("type", Git.RevisionSearchQuery.TypeKey(q.Type))
            .Set("scope", Git.RevisionSearchQuery.ScopeKey(q.Scope))
            .Set("search_string", q.SearchString)).ToList());
    }

    /// <summary>決まった順序で全キーを埋める（新規作成時のファイルの形を決める）。</summary>
    public void EnsureDefaults()
    {
        if (!_doc.Has("pinned_references")) PinnedReferences = [];
        if (!_doc.Has("sign_off")) SignOff = false;
        if (!_doc.Has("tab_width")) TabWidth = 4;
        if (!_doc.Has("show_bugtracker_links")) ShowBugtrackerLinks = true;
        if (!_doc.Has("trust_shared_commands")) TrustSharedCommands = false;
        if (!_doc.Has("lean_branching_no_fast_forward")) LeanBranchingNoFastForward = false;
        if (!_doc.Has("gitignore_suggestion_dismissed")) GitignoreSuggestionDismissed = false;
        if (!_doc.Has("pull_requests_recent_search_queries")) PullRequestsRecentSearchQueries = [];
        if (!_doc.Has("issues_recent_search_queries")) IssuesRecentSearchQueries = [];
        if (!_doc.Has("recent_revision_search_queries")) RecentRevisionSearchQueries = [];
    }
}

/// <summary>worktree ごとの表示設定 `.git/gitknife/worktree-config.toml`。</summary>
public sealed class WorktreeConfig
{
    public const string FileName = "worktree-config.toml";

    private readonly TomlDocument _doc;
    private readonly string _path;

    private WorktreeConfig(TomlDocument doc, string path) { _doc = doc; _path = path; }

    /// <param name="gitDirectory">この worktree の git ディレクトリ（メインなら .git、追加 worktree なら .git/worktrees/&lt;name&gt;）。</param>
    public static WorktreeConfig Load(string gitDirectory)
    {
        var path = Path.Combine(gitDirectory, ProductInfo.RepositoryConfigDirectoryName, FileName);
        return new WorktreeConfig(TomlDocument.Load(path), path);
    }

    public void Save() => _doc.Save(_path);

    public string DraftMessage { get => _doc.GetString("draft_message") ?? ""; set => _doc.Set("draft_message", value); }
    public bool HideStashes { get => _doc.GetBool("hide_stashes", false); set => _doc.Set("hide_stashes", value); }
    public bool HideTags { get => _doc.GetBool("hide_tags", false); set => _doc.Set("hide_tags", value); }
    public bool HideUntrackedFiles { get => _doc.GetBool("hide_untracked_files", false); set => _doc.Set("hide_untracked_files", value); }
    public IReadOnlyList<string> FilterReferences { get => _doc.GetStringArray("filter_references"); set => _doc.Set("filter_references", value); }
    public IReadOnlyList<string> HiddenReferences { get => _doc.GetStringArray("hidden_references"); set => _doc.Set("hidden_references", value); }
    public bool CollapseAllMode { get => _doc.GetBool("collapse_all_mode", false); set => _doc.Set("collapse_all_mode", value); }
    public IReadOnlyList<string> CollapseToggledShas { get => _doc.GetStringArray("collapse_toggled_shas"); set => _doc.Set("collapse_toggled_shas", value); }
    /// <summary>
    /// サイドバーで開いている節（例：["Branches/bugfix", "Branches/feature", "Remotes", "Stashes"]。見出し Branches は入らない）。
    /// キーが無ければ null（GitKnife の既定の開き方のまま）。
    /// </summary>
    public IReadOnlyList<string>? ExpandedSidebarItems { get => _doc.Has("expanded_sidebar_items") ? _doc.GetStringArray("expanded_sidebar_items") : null; set => _doc.Set("expanded_sidebar_items", value ?? []); }

    public void EnsureDefaults()
    {
        if (!_doc.Has("draft_message")) DraftMessage = "";
        if (!_doc.Has("hide_stashes")) HideStashes = false;
        if (!_doc.Has("hide_tags")) HideTags = false;
        if (!_doc.Has("hide_untracked_files")) HideUntrackedFiles = false;
        if (!_doc.Has("filter_references")) FilterReferences = [];
        if (!_doc.Has("hidden_references")) HiddenReferences = [];
        if (!_doc.Has("collapse_all_mode")) CollapseAllMode = false;
        if (!_doc.Has("collapse_toggled_shas")) CollapseToggledShas = [];
    }
}
