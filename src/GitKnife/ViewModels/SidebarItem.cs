using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels;

public enum SidebarItemKind { Group, Folder, LocalBranch, Remote, RemoteBranch, Tag, Stash, Submodule, Worktree, ShowMore }

/// <summary>
/// サイドバーの 1 行。グループ・フォルダ・項目を 1 本のリストに平らに並べ、インデントで木を表す。
/// 開閉状態を持ち、<see cref="SidebarViewModel"/> が見えている行だけを <see cref="SidebarViewModel.Rows"/> に出す。
/// </summary>
public sealed partial class SidebarItem : ObservableObject
{
    public required SidebarItemKind Kind { get; init; }
    public required string Title { get; init; }
    public required int Level { get; init; }
    /// <summary>開閉のキー（グループ名やフォルダのパス）。リポジトリごとに記憶する。</summary>
    public required string Key { get; init; }
    public ReferenceInfo? Reference { get; init; }
    public StashInfo? Stash { get; init; }
    public RemoteInfo? Remote { get; init; }
    public Core.Git.SubmoduleInfo? Submodule { get; init; }
    /// <summary>行に出す名前（submodule は親から見て変わっていると "sublib*"。status を読み直すたびに変える）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayTitle))] private string _titleSuffix = "";
    public string DisplayTitle => Title + TitleSuffix;
    public List<SidebarItem> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Filter / Hide のボタンが付けるパターン（ブランチは完全名、フォルダは末尾 "/"。ReferenceFilter）。空ならボタンを出さない。</summary>
    public IReadOnlyList<string> FilterPatterns { get; set; } = [];
    public bool CanFilter => FilterPatterns.Count > 0;
    [ObservableProperty] private bool _isFilterActive;
    /// <summary>ブランチの行の PinButton（config.toml の pinned_references）。</summary>
    public bool CanPin => Kind is SidebarItemKind.LocalBranch or SidebarItemKind.RemoteBranch;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isHiddenActive;
    /// <summary>FilterButton のツールチップ（"Show 'feature' commits only"）。</summary>
    public string FilterToolTip => $"Show '{Title}' commits only";
    public string HideToolTip => $"Hide '{Title}' commits";

    public bool CanExpand => Kind is SidebarItemKind.Group or SidebarItemKind.Folder or SidebarItemKind.Remote;
    public bool HasChildren => Children.Count > 0;
    /// <summary>開閉のマーク（⌄ / ›）を出すか。中身の無い節（Tags / Stashes / Submodules が空のとき）は出さない（2026-10-27 ユーザーの指摘）。</summary>
    public bool ShowExpander => CanExpand && HasChildren;
    public bool IsCurrentBranch => Reference?.IsHead == true;

    /// <summary>アイコンの AutomationId（BranchImage、FolderImage など）。</summary>
    public string ImageId => Kind switch
    {
        SidebarItemKind.Folder => "FolderImage",
        SidebarItemKind.LocalBranch => "BranchImage",
        SidebarItemKind.RemoteBranch => "RemoteBranchImage",
        SidebarItemKind.Remote => "RemoteImage",
        SidebarItemKind.Tag => "TagImage",
        SidebarItemKind.Stash => "StashImage",
        SidebarItemKind.Worktree => "WorktreeImage",
        SidebarItemKind.Submodule => "SubmoduleImage",
        _ => "",
    };

    /// <summary>右側の "1↑ 1↓"。</summary>
    public string TrackText
    {
        get
        {
            if (Reference is null || Kind != SidebarItemKind.LocalBranch) return "";
            var parts = new List<string>();
            if (Reference.Ahead > 0) parts.Add($"{Reference.Ahead}↑");
            if (Reference.Behind > 0) parts.Add($"{Reference.Behind}↓");
            return string.Join(' ', parts);
        }
    }

    /// <summary>ToolTip。タグのメッセージ、ブランチの upstream など。</summary>
    public string? ToolTipText => Kind switch
    {
        SidebarItemKind.Tag when Reference is { IsAnnotated: true } => Reference.Subject,
        SidebarItemKind.LocalBranch when Reference?.Upstream is { } up => up.Replace("refs/remotes/", ""),
        SidebarItemKind.Remote => Remote?.FetchUrl,
        _ => null,
    };
}
