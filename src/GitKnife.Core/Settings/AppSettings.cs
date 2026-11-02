using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitKnife.Core.Settings;

/// <summary>
/// %LocalAppData%\GitKnife\settings.json のモデル。
/// 知らないキーは <see cref="Extra"/> に保持して書き戻す（新しい版や他のツールが足したキーを壊さない）。
/// </summary>
public sealed class AppSettings
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();

    // --- 更新・全般 ---
    public int ApplicationUpdateType { get; set; }
    public int ActivityManagerViewMode { get; set; } = 1;
    public int LayoutScaling { get; set; } = 100;
    public bool DisableHardwareAcceleration { get; set; }
    public bool FetchRemotesAutomatically { get; set; } = true;
    public int AutomaticStatusUpdateInterval { get; set; } = 60;
    public int MaxCommitCount { get; set; } = 50000;
    public string? LastUpdateCheck { get; set; }

    // --- テーマ・表示 ---
    public int Theme { get; set; } = 1;
    public bool FollowSystemTheme { get; set; } = true;
    public int RevisionListOrientation { get; set; } = 1;
    public int MergerLayoutOrientation { get; set; }
    public int FileListMode { get; set; } = 1;
    public int RevisionSortOrder { get; set; } = 1;
    public int LocalBranchSortOrder { get; set; } = 1;
    public int RemoteBranchSortOrder { get; set; } = 1;
    public int TagSortOrder { get; set; } = 2;
    public bool CompactBranchLabels { get; set; } = true;
    public bool ShowWorktrees { get; set; } = true;

    // --- Git ---
    public string? GitInstancePath { get; set; }
    public bool FetchAllTags { get; set; }
    public bool RebaseAutostash { get; set; } = true;
    public bool RebaseUpdateRefs { get; set; }
    public bool UpdateSubmodulesOnCheckout { get; set; } = true;
    public bool PushAutomaticallyOnCommit { get; set; }
    public bool VerboseGitOutput { get; set; }
    public List<JsonElement> SshKeys { get; set; } = [];

    // --- コミット ---
    public int CommitSubjectLowLimit { get; set; } = 50;
    public int CommitSubjectHighLimit { get; set; } = 70;
    public int CommitSpellCheckingMode { get; set; }
    public string ReferenceSpaceCharacterReplacement { get; set; } = "-";
    public bool OpenAiLoggedIn { get; set; }

    // --- diff 表示 ---
    public bool DiffIgnoreWhitespaces { get; set; }
    public bool DiffShowHiddenSymbols { get; set; }
    public bool DiffWordWrap { get; set; }
    public bool DiffShowChangeMarks { get; set; }
    public bool DiffShowEntireFile { get; set; }
    public bool RevisionWindowDiffShowEntireFile { get; set; }
    public int DiffContextSize { get; set; } = 19;
    public int CodeEditorFontSize { get; set; } = 13;
    public int PageGuideLinePosition { get; set; } = 72;
    public bool ImageDiffHighlightPixels { get; set; }
    public bool DisableSyntaxHighlighting { get; set; }
    /// <summary>Preferences > Integration の "Highlight issue tracker links in commit messages"。</summary>
    public bool ShowBugtrackerLinks { get; set; } = true;
    public int RevisionDiffLayoutMode { get; set; } = 1;
    public int RevisionWindowDiffLayoutMode { get; set; }
    public int PopupDiffLayoutMode { get; set; } = 1;
    public int CommitDiffLayoutMode { get; set; } = 1;
    public int HistoryDiffLayoutMode { get; set; } = 1;

    // --- ダイアログの前回の選択 ---
    public bool Fetch_FetchAllRemotes { get; set; } = true;
    public bool Pull_Rebase { get; set; } = true;
    public bool Pull_StashAndReapply { get; set; } = true;
    public bool Push_PushAllTags { get; set; }
    public bool CreateTag_Push { get; set; }
    public bool CreateBranch_Checkout { get; set; } = true;
    public bool Checkout_StashAndReapply { get; set; }
    public bool CheckoutAndSync_StashAndReapply { get; set; }
    public bool SaveStash_StageNewFiles { get; set; }
    public bool ApplyStash_DeleteAfterApply { get; set; } = true;
    public bool InteractiveRebase_CreateBackup { get; set; }
    public bool InteractiveRebase_UpdateRefs { get; set; }
    public bool GitFlowFinishFeature_DeleteBranches { get; set; } = true;
    public bool GitFlowFinishFeature_Rebase { get; set; }
    public bool GitFlowFinishFeature_NoFastForward { get; set; }
    public bool GitFlowFinishRelease_DeleteBranches { get; set; } = true;
    public bool GitFlowFinishRelease_BackMergeMaster { get; set; } = true;
    public bool GitFlowFinishHotfix_DeleteBranches { get; set; } = true;
    /// <summary>Merge ダイアログの Merge Option。1 = --no-ff。</summary>
    public int MergeType { get; set; } = 1;

    // --- ウィンドウ・ペイン ---
    public WindowLocationState MainWindowLocationState { get; set; } = new();
    public WindowLocationState RevisionWindowLocationState { get; set; } = new();
    public WindowLocationState RevisionPopUpWindowLocationState { get; set; } = new();
    public WindowLocationState SideBySideMergeWindowLocationState { get; set; } = new();
    public WindowLocationState InteractiveRebaseWindowLocationState { get; set; } = new();
    public WindowLocationState BlameWindowLocationState { get; set; } = new();
    public WindowLocationState HistoryWindowLocationState { get; set; } = new();
    public WindowLocationState AiResultWindowLocationState { get; set; } = new();
    public int CommitViewColumnWidth { get; set; } = 382;
    public int CommitViewCombinedListLocationColumnWidth { get; set; } = 120;
    public int SidebarColumnWidth { get; set; } = 339;
    public bool TabListSidebarCollapsed { get; set; } = true;
    public int TabListSidebarWidth { get; set; } = 180;
    public int RevisionDetailsChangesColumnWidth { get; set; } = 280;
    public int RevisionDetailsFileTreeColumnWidth { get; set; } = 394;
    public bool RevisionDetailsPaneCollapsed { get; set; }
    public int AiResultColumnWidth { get; set; } = 350;
    public int VerticalLayoutRevisionListViewWidth { get; set; } = 350;
    public int RepositoryManagerTreeViewColumnWidth { get; set; } = 350;
    public JsonElement? RepositoryManagerTreeViewExpandedItems { get; set; }

    // --- 外部ツール ---
    public List<ExternalToolSettings> ExternalMergeTools { get; set; } = [];
    public ExternalToolSettings MergeTool { get; set; } = new();
    public List<ExternalToolSettings> ExternalDiffTools { get; set; } = [];
    public ExternalToolSettings ExternalDiffTool { get; set; } = new();
    public ExternalToolSettings? ShellTool { get; set; }
    public string? RecentPatchDirectory { get; set; }
    public string? LastFeedbackEmail { get; set; }

    // --- Repository Manager とワークスペース ---
    public RepositoryManagerSettings RepositoryManager { get; set; } = new();
    public WorkspacesSettings Workspaces { get; set; } = new();

    // --- GitKnife だけのキー ---
    /// <summary>バグレポート（Ctrl+Shift+F12）の送り先。トークンは settings.json に書かず、Windows の資格情報マネージャーに置く。</summary>
    public BugReportSettings BugReport { get; set; } = new();
    /// <summary>更新の確認（File > Check for Updates...、1 日 1 回）。最後に確かめた日時は LastUpdateCheck。トークンは資格情報マネージャー。</summary>
    public UpdateSettings Update { get; set; } = new();
    /// <summary>
    /// 登録した SSH のホスト（T1301。Preferences > Hosts）。リポジトリは \\gitknife-ssh\&lt;Name&gt;\… で表し、git は ssh で Destination に入って動かす。
    /// WSL はパスで決まるので登録しない。
    /// </summary>
    public List<SshHostSettings> SshHosts { get; set; } = [];
    /// <summary>File > Open Repository... で最後に開いた場所（"This PC"、"WSL: …"、"SSH: …"）。次に開いたときの最初の選択。</summary>
    public string OpenRepositoryLocation { get; set; } = "";

    /// <summary>モデルにないキー。読んだまま書き戻す。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class WindowLocationState
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>WPF の WindowState（0 Normal、1 Minimized、2 Maximized）。</summary>
    public int WindowState { get; set; }
}

public sealed class ExternalToolSettings
{
    public string Type { get; set; } = "";
    public string ApplicationPath { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public sealed class RepositoryManagerSettings
{
    public List<string> SourceDirectories { get; set; } = [];
    public List<RepositoryCategory> Categories { get; set; } = [];
    public List<RepositoryEntry> Repositories { get; set; } = [];
    public int ScanDepth { get; set; } = 3;
}

public sealed class RepositoryCategory
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class RepositoryEntry
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string GitDirectoryPath { get; set; } = "";
    public string? LastAccessTime { get; set; }
}

public sealed class WorkspacesSettings
{
    public List<Workspace> All { get; set; } = [];
    public string ActiveWorkspace { get; set; } = "Home";
    public bool ShowInTitle { get; set; }
}

public sealed class BugReportSettings
{
    /// <summary>Forgejo か GitHub の Web の URL（空なら送らずに保存だけ）。既定は配布先（<see cref="Distribution"/>）。</summary>
    public string ServerUrl { get; set; } = Distribution.ServerUrl;
    /// <summary>"owner/repo"。</summary>
    public string Repository { get; set; } = Distribution.Repository;
    /// <summary>Windows が信頼しない証明書でも送る（社内の自己署名の証明書のサーバー用。既定は無効）。</summary>
    public bool AllowUntrustedCertificate { get; set; }
    /// <summary>作った Issue の担当にするユーザー（Report a Bug の "Assign to …"。#8。既定は配布先の値。Forgejo のときだけ使う）。</summary>
    public string Assignee { get; set; } = Distribution.BugReportAssignee;
    /// <summary>"Assign to …" のチェック（前回の状態を覚える）。</summary>
    public bool AssignIssue { get; set; } = true;
}

public sealed class SshHostSettings
{
    /// <summary>GitKnife の中の名前（パス \\gitknife-ssh\&lt;Name&gt;\… に入る。/ と \ は使えない）。</summary>
    public string Name { get; set; } = "";
    /// <summary>ssh に渡す接続先（"user@host"、~/.ssh/config の Host 名。ポートは config で）。空なら Name。</summary>
    public string Destination { get; set; } = "";
    /// <summary>接続先の git（空なら PATH の git）。</summary>
    public string GitPath { get; set; } = "";

    /// <summary>Name に使える形（/ と \ を除く）。</summary>
    public static string CleanName(string name) => name.Replace("/", "").Replace("\\", "").Trim();

    /// <summary>接続先から名前を作る（"user@build01" → "build01"）。</summary>
    public static string NameFromDestination(string destination) => CleanName(destination[(destination.LastIndexOf('@') + 1)..]);

    public Git.SshGitHost ToHost() => new(Name, Destination.Trim().Length > 0 ? Destination.Trim() : Name, GitPath.Trim().Length > 0 ? GitPath.Trim() : "git");
}

public sealed class UpdateSettings
{
    /// <summary>リリースを置く Forgejo か GitHub の Web の URL。既定は配布先（<see cref="Distribution"/>）。</summary>
    public string ServerUrl { get; set; } = Distribution.ServerUrl;
    /// <summary>"owner/repo"。</summary>
    public string Repository { get; set; } = Distribution.Repository;
    /// <summary>
    /// Forgejo に登録した OAuth2 アプリの Client ID。空なら配布先の <see cref="Distribution.OAuthClientId"/>
    /// （既定値を settings.json に書き込まず、前の版が空のまま保存していても組み込みの値が使われるように）。
    /// 両方空ならサインインせずに読む（GitHub と公開の Forgejo）。
    /// </summary>
    public string OAuthClientId { get; set; } = "";
    [JsonIgnore] public string EffectiveOAuthClientId => string.IsNullOrWhiteSpace(OAuthClientId) ? Distribution.OAuthClientId : OAuthClientId.Trim();
    /// <summary>OAuth2 で求める権限（空なら指定しない）。</summary>
    public string OAuthScope { get; set; } = "read:repository";
    /// <summary>起動時に 1 日 1 回確かめる。</summary>
    public bool CheckAutomatically { get; set; } = true;
}

public sealed class Workspace
{
    public string Name { get; set; } = "";
    public List<string> Repositories { get; set; } = [];
    public string? ActiveRepository { get; set; }
}
