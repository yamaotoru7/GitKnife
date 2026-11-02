using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels;

/// <summary>サイドバーの参照ツリー。</summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly List<SidebarItem> _roots = [];
    private readonly HashSet<string> _collapsed = [];

    public ObservableCollection<SidebarItem> Rows { get; } = [];

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private SidebarItem? _selectedItem;

    partial void OnFilterTextChanged(string value) => RebuildRows();

    /// <summary>行を選んだ（読み直しで選び直したときは出さない）。一覧をそのコミットへ動かす。</summary>
    public event Action<SidebarItem>? Navigate;
    private bool _rebuilding;

    partial void OnSelectedItemChanged(SidebarItem? value)
    {
        if (!_rebuilding && value is not null) Navigate?.Invoke(value);
    }

    /// <summary>親から見て変わっている submodule に "*" を付ける（status だけ読み直したときも呼ぶ）。</summary>
    public void SetChangedSubmodules(ISet<string> changed)
    {
        if (_roots.FirstOrDefault(r => r.Key == "group:Submodules") is not { } group) return;
        foreach (var item in group.Children) item.TitleSuffix = item.Submodule is { } sm && changed.Contains(sm.Path) ? "*" : "";
    }

    /// <summary>複数の行を選ばせる（ListBox.SelectedItems はバインドできないのでビューが受ける）。</summary>
    public event Action<IReadOnlyList<SidebarItem>>? SelectRequested;

    /// <summary>
    /// 見出し Branches の Select Stale Local Branches：upstream が消えたローカルブランチ（[gone]）を選ぶ。
    /// マージ済みでも upstream が残っている・upstream が無いものは選ばない。フォルダは開く。
    /// </summary>
    public void SelectStaleLocalBranches()
    {
        if (_roots.FirstOrDefault(r => r.Key == "group:Branches") is not { } group) return;
        var stale = new List<SidebarItem>();
        void Walk(SidebarItem item, List<SidebarItem> path)
        {
            if (item.Kind == SidebarItemKind.LocalBranch && item.Reference is { UpstreamGone: true })
            {
                stale.Add(item);
                foreach (var p in path) _collapsed.Remove(p.Key);
            }
            path.Add(item);
            foreach (var c in item.Children) Walk(c, path);
            path.RemoveAt(path.Count - 1);
        }
        Walk(group, []);
        if (stale.Count == 0) return;
        RebuildRows();
        SelectedItem = stale[0];
        SelectRequested?.Invoke(stale);
    }

    /// <summary>
    /// 並び順（設定 LocalBranchSortOrder / RemoteBranchSortOrder / TagSortOrder）。1 = Alphabetically、2 = Recently used（コミットの新しい順）、3 = Alphabetically backward。
    /// 既定はブランチ 1、タグ 2。
    /// </summary>
    public static IEnumerable<ReferenceInfo> Sort(IEnumerable<ReferenceInfo> refs, int order, IReadOnlyDictionary<string, DateTimeOffset> dates) => order switch
    {
        2 => refs.OrderByDescending(r => dates.TryGetValue(r.TargetSha, out var d) ? d : DateTimeOffset.MinValue).ThenBy(r => r.ShortName, NaturalComparer.Instance),
        3 => refs.OrderByDescending(r => r.ShortName, NaturalComparer.Instance),
        _ => refs.OrderBy(r => r.ShortName, NaturalComparer.Instance),
    };

    public void Load(RepositoryReferences refs, Core.Settings.AppSettings settings, IReadOnlyDictionary<string, DateTimeOffset> dates, string repositoryPath,
        IReadOnlyList<string>? filter = null, IReadOnlyList<string>? hidden = null, IReadOnlyList<string>? pinned = null,
        IReadOnlyList<Core.Git.SubmoduleInfo>? submodules = null, ISet<string>? changedSubmodules = null)
    {
        filter ??= [];
        hidden ??= [];
        pinned ??= [];
        _roots.Clear();
        // Worktrees（Branches より上。ほかの worktree でチェックアウトしているブランチ。このタブ自身の worktree は出さない）
        var worktrees = refs.LocalBranches
            .Where(b => b.WorktreePath is { } p && !string.Equals(Normalize(p), Normalize(repositoryPath), StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.ShortName, NaturalComparer.Instance).ToList();
        if (worktrees.Count > 0)
        {
            var group = Group("Worktrees");
            foreach (var w in worktrees)
                group.Children.Add(new SidebarItem { Kind = SidebarItemKind.Worktree, Title = w.ShortName, Level = 1, Key = "worktree:" + w.WorktreePath, Reference = w });
            _roots.Add(group);
        }
        // Pinned（Worktrees と Branches の間。フォルダの形はそのまま）
        var pinnedRefs = refs.References.Where(r => pinned.Contains(r.CanonicalName) && r.Kind is ReferenceKind.LocalBranch or ReferenceKind.RemoteBranch).ToList();
        if (pinnedRefs.Count > 0)
        {
            var group = Group("Pinned");
            foreach (var r in pinnedRefs.OrderBy(r => r.ShortName, NaturalComparer.Instance))
                AddPath(group, r.ShortName, r, r.Kind == ReferenceKind.LocalBranch ? SidebarItemKind.LocalBranch : SidebarItemKind.RemoteBranch, 1, "pinned:");
            _roots.Add(group);
        }
        var branches = Group("Branches");
        foreach (var b in Sort(refs.LocalBranches, settings.LocalBranchSortOrder, dates)) AddPath(branches, b.ShortName, b, SidebarItemKind.LocalBranch, 1);
        _roots.Add(branches);

        var remotes = Group("Remotes");
        foreach (var remote in refs.Remotes.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = new SidebarItem { Kind = SidebarItemKind.Remote, Title = remote.Name, Level = 1, Key = "remote:" + remote.Name, Remote = remote };
            foreach (var rb in Sort(refs.RemoteBranches.Where(r => r.RemoteName == remote.Name), settings.RemoteBranchSortOrder, dates))
                AddPath(node, rb.ShortName[(remote.Name.Length + 1)..], rb, SidebarItemKind.RemoteBranch, 2, "remote:" + remote.Name + "/");
            remotes.Children.Add(node);
        }
        _roots.Add(remotes);

        // タグが多いときは最初の 20 個と "Show all tags (39)" の行。押すとすべて出して最後の行が "Show less" になり、押すと戻る
        // （タブを閉じるまで覚える）
        var tags = Group("Tags");
        _allTags = Sort(refs.Tags, settings.TagSortOrder, dates)
            .Select(t => new SidebarItem { Kind = SidebarItemKind.Tag, Title = t.ShortName, Level = 1, Key = t.CanonicalName, Reference = t }).ToList();
        FillTags(tags);
        _roots.Add(tags);

        var stashes = Group("Stashes");
        foreach (var s in refs.Stashes)
            stashes.Children.Add(new SidebarItem { Kind = SidebarItemKind.Stash, Title = StashTitle(s.Message), Level = 1, Key = "stash:" + s.Sha, Stash = s });
        _roots.Add(stashes);

        // Submodules（.gitmodules の path。親のリポジトリから見て変わっている（コミットしていない）ものは "sublib*"）
        var subs = Group("Submodules");
        foreach (var sm in (submodules ?? []).OrderBy(s => s.Path, NaturalComparer.Instance))
            subs.Children.Add(new SidebarItem
            {
                Kind = SidebarItemKind.Submodule, Level = 1, Key = "submodule:" + sm.Path, Submodule = sm, Title = sm.Path,
            });
        _roots.Add(subs);
        SetChangedSubmodules(changedSubmodules ?? new HashSet<string>());

        // Filter / Hide のパターン（ブランチの行とフォルダの行）
        var remoteNames = refs.Remotes.Select(r => r.Name).ToList();
        foreach (var item in _roots.SelectMany(Descendants))
        {
            item.FilterPatterns = item.Kind switch
            {
                SidebarItemKind.LocalBranch or SidebarItemKind.RemoteBranch when item.Reference is { } r => [r.CanonicalName],
                SidebarItemKind.Folder when item.Key.StartsWith("pinned:", StringComparison.Ordinal) => [],
                SidebarItemKind.Remote when item.Remote is { } rm => [$"refs/remotes/{rm.Name}/"],
                SidebarItemKind.Folder when item.Key.StartsWith("folder:", StringComparison.Ordinal) => Core.Git.ReferenceFilter.PatternsFor("refs/heads/" + item.Key["folder:".Length..], true, remoteNames),
                SidebarItemKind.Folder when item.Key.StartsWith("remote:", StringComparison.Ordinal) => Core.Git.ReferenceFilter.PatternsFor("refs/remotes/" + item.Key["remote:".Length..], true, []),
                _ => [],
            };
            item.IsFilterActive = item.CanFilter && item.FilterPatterns.All(filter.Contains);
            item.IsPinned = item.Reference is { } pr && pinned.Contains(pr.CanonicalName);
            item.IsHiddenActive = item.CanFilter && item.FilterPatterns.All(hidden.Contains);
        }
        ApplySavedExpanded();
        RebuildRows();
    }

    private const int TagLimit = 20;
    private bool _showAllTags;
    private List<SidebarItem> _allTags = [];

    private void FillTags(SidebarItem tags)
    {
        tags.Children.Clear();
        if (_allTags.Count <= TagLimit) { tags.Children.AddRange(_allTags); return; }
        tags.Children.AddRange(_showAllTags ? _allTags : _allTags.Take(TagLimit));
        tags.Children.Add(new SidebarItem
        {
            Kind = SidebarItemKind.ShowMore, Level = 1, Key = "show-all-tags",
            Title = _showAllTags ? "Show less" : $"Show all tags ({_allTags.Count})",
        });
    }

    /// <summary>"Show all tags (N)" / "Show less" を押した：すべて出すか、最初の 20 個に戻す。</summary>
    public void ToggleAllTags()
    {
        _showAllTags = !_showAllTags;
        if (_roots.FirstOrDefault(r => r.Key == "group:Tags") is not { } tags) return;
        FillTags(tags);
        RebuildRows();
    }

    private static IEnumerable<SidebarItem> Descendants(SidebarItem item) => item.Children.SelectMany(c => (IEnumerable<SidebarItem>)[c, .. Descendants(c)]);

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');

    /// <summary>"On main: WIP: greeting experiment" → "WIP: greeting experiment"（サイドバーの表示）。</summary>
    public static string StashTitle(string message)
    {
        foreach (var prefix in new[] { "On ", "WIP on " })
        {
            if (!message.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var colon = message.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0) return message[(colon + 2)..];
        }
        return message;
    }

    private static SidebarItem Group(string name) => new() { Kind = SidebarItemKind.Group, Title = name, Level = 0, Key = "group:" + name };

    /// <summary>"feature/login" をフォルダ "feature" の下の "login" にする。フォルダはブランチより先に、名前順に並べる。</summary>
    private static void AddPath(SidebarItem parent, string path, ReferenceInfo reference, SidebarItemKind kind, int level, string keyPrefix = "folder:")
    {
        var parts = path.Split('/');
        var node = parent;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var key = keyPrefix + string.Join('/', parts[..(i + 1)]);
            var folder = node.Children.FirstOrDefault(c => c.Kind == SidebarItemKind.Folder && c.Key == key);
            if (folder is null)
            {
                folder = new SidebarItem { Kind = SidebarItemKind.Folder, Title = parts[i], Level = level + i, Key = key };
                node.Children.Add(folder);
            }
            node = folder;
        }
        node.Children.Add(new SidebarItem { Kind = kind, Title = parts[^1], Level = level + parts.Length - 1, Key = reference.CanonicalName, Reference = reference });
        node.Children.Sort((a, b) =>
        {
            var fa = a.Kind == SidebarItemKind.Folder ? 0 : 1;
            var fb = b.Kind == SidebarItemKind.Folder ? 0 : 1;
            return fa != fb ? fa.CompareTo(fb) : NaturalComparer.Instance.Compare(a.Title, b.Title);
        });
    }

    public void Toggle(SidebarItem item)
    {
        if (!item.CanExpand) return;
        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded) _collapsed.Remove(item.Key); else _collapsed.Add(item.Key);
        RebuildRows();
        ExpandedChanged?.Invoke();
    }

    // ---- 開閉の保存（worktree-config.toml の expanded_sidebar_items）----

    /// <summary>開閉を変えた（RepositoryViewModel が worktree-config.toml に書く）。</summary>
    public event Action? ExpandedChanged;
    private IReadOnlyList<string>? _savedExpanded;

    /// <summary>保存されていた開いている節。次の Load で一度だけ当てる。</summary>
    public void UseSavedExpanded(IReadOnlyList<string>? expanded) => _savedExpanded = expanded;

    /// <summary>
    /// 保存する名前：見出しは "Remotes"、ブランチのフォルダは "Branches/feature"、リモートは "Remotes/origin"、リモートのフォルダは "Remotes/origin/bugfix"。
    /// Pinned などそれ以外は保存しない（null）。
    /// </summary>
    private static string? TreeStatePath(SidebarItem item) => item.Key switch
    {
        var k when k.StartsWith("group:", StringComparison.Ordinal) => k["group:".Length..],
        var k when k.StartsWith("folder:", StringComparison.Ordinal) => "Branches/" + k["folder:".Length..],
        var k when k.StartsWith("remote:", StringComparison.Ordinal) => "Remotes/" + k["remote:".Length..],
        _ => null,
    };

    private IEnumerable<SidebarItem> Expandables()
    {
        IEnumerable<SidebarItem> Walk(SidebarItem i) => new[] { i }.Concat(i.Children.SelectMany(Walk));
        return _roots.SelectMany(Walk).Where(i => i.CanExpand);
    }

    /// <summary>今開いている節（見出し Branches は入れない）。</summary>
    public IReadOnlyList<string> ExpandedPaths()
        => Expandables().Where(i => !_collapsed.Contains(i.Key)).Select(TreeStatePath).OfType<string>().Where(p => p != "Branches").ToList();

    /// <summary>これまでに出した節。後から出てきた節（fetch で増えたリモートのフォルダなど）を見分ける。</summary>
    private readonly HashSet<string> _seenExpandables = [];

    /// <summary>
    /// 保存の無いリポジトリを初めて開いたときは全部開く。保存があれば載っている節だけ開く。
    /// 最初の読み込みの後に出てきた節は閉じておく（expanded_sidebar_items に無い節は閉じる。
    /// 2026-10-30 llama.cpp で fetch で増えたリモートのフォルダが全部開いていた）。
    /// </summary>
    private void ApplySavedExpanded()
    {
        var saved = _savedExpanded;
        _savedExpanded = null;
        var open = saved?.ToHashSet(StringComparer.Ordinal);
        var first = _seenExpandables.Count == 0;
        foreach (var item in Expandables())
        {
            var isNew = _seenExpandables.Add(item.Key);
            // 見出し Branches は保存されないので開いたままにする（推定）
            if (TreeStatePath(item) is not { } path || path == "Branches") continue;
            if (open is not null) { if (open.Contains(path)) _collapsed.Remove(item.Key); else _collapsed.Add(item.Key); }
            else if (isNew && !first) _collapsed.Add(item.Key);
        }
    }

    private void RebuildRows()
    {
        var selectedKey = SelectedItem?.Key;
        _rebuilding = true;
        try
        {
            Rows.Clear();
            var filter = FilterText.Trim();
            foreach (var root in _roots) Add(root, filter);
            SelectedItem = selectedKey is null ? null : Rows.FirstOrDefault(r => r.Key == selectedKey);
        }
        finally { _rebuilding = false; }
    }

    private void Add(SidebarItem item, string filter)
    {
        item.IsExpanded = !_collapsed.Contains(item.Key);
        if (filter.Length > 0 && item.Kind != SidebarItemKind.Group && !Matches(item, filter)) return;
        Rows.Add(item);
        if (!item.IsExpanded && filter.Length == 0) return;
        foreach (var child in item.Children) Add(child, filter);
    }

    private static bool Matches(SidebarItem item, string filter)
        => item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) || item.Children.Any(c => Matches(c, filter));
}

/// <summary>数字を数値として比べる（v0.10 が v0.9 の後に来る）。</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i; while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j; while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0'); var b = y[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
