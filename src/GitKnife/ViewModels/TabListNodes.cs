namespace GitKnife.ViewModels;

/// <summary>縦タブ（TabListSidebar）の見出しの行：今のワークスペース名。</summary>
public sealed record TabListWorkspaceNode(string Name);

/// <summary>縦タブのタブの行。</summary>
public sealed record TabListTabNode(RepositoryViewModel Tab);
