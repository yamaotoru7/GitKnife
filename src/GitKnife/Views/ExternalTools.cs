using System.Diagnostics;
using System.IO;

namespace GitKnife.Views;

/// <summary>
/// 外部の diff / マージツール（Preferences > Integration の External Merge Tools / External Diff Tools と、右クリックの External Diff / External Merge）。
/// 引数の書式：$REMOTE が古い方（左）、$LOCAL が新しい方（右）。マージは $REMOTE = theirs、$LOCAL = ours、$BASE、$MERGED。
/// Path が空なら入っていない（入っていないツールも一覧に出し、パスを空にする）。DiffArguments が null ならマージ専用（YAMLMerge）。
/// </summary>
public sealed record ExternalDiffTool(string Name, string Path, string? DiffArguments, string? MergeArguments)
{
    public bool IsInstalled => Path.Length > 0 && File.Exists(Path);

    public void Open(string left, string right)
    {
        var args = (DiffArguments ?? "\"$REMOTE\" \"$LOCAL\"").Replace("$REMOTE", left).Replace("$LOCAL", right);
        Process.Start(new ProcessStartInfo(Path, args) { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>外部のマージツールを開き、終わるまで待つ（$REMOTE = theirs、$LOCAL = ours、$BASE = 共通の祖先、$MERGED = 作業ツリーのファイル）。</summary>
    public Task MergeAsync(string remote, string local, string @base, string merged)
    {
        var args = (MergeArguments ?? "\"$REMOTE\" \"$LOCAL\" \"$BASE\" \"$MERGED\"")
            .Replace("$REMOTE", remote).Replace("$LOCAL", local).Replace("$BASE", @base).Replace("$MERGED", merged);
        var p = Process.Start(new ProcessStartInfo(Path, args) { UseShellExecute = false, CreateNoWindow = true });
        return p is null ? Task.CompletedTask : p.WaitForExitAsync();
    }
}

public static class ExternalTools
{
    private static IReadOnlyList<ExternalDiffTool>? _catalog;

    /// <summary>
    /// 決まったツールの一覧（Merge は 10、Diff は YAMLMerge を除く 9）。
    /// </summary>
    public static IReadOnlyList<ExternalDiffTool> Catalog => _catalog ??= BuildCatalog();

    /// <summary>右クリックの External Diff に出すもの（入っているツールと、設定 ExternalDiffTools に登録したもの）。</summary>
    public static IReadOnlyList<ExternalDiffTool> DiffTools => Catalog.Where(t => t.DiffArguments is not null && t.IsInstalled).Concat(Custom(App.Settings.ExternalDiffTools)).ToList();

    /// <summary>右クリックの External Merge に出すもの。</summary>
    public static IReadOnlyList<ExternalDiffTool> MergeTools => Catalog.Where(t => t.MergeArguments is not null && t.IsInstalled).Concat(Custom(App.Settings.ExternalMergeTools)).ToList();

    private static IEnumerable<ExternalDiffTool> Custom(IEnumerable<Core.Settings.ExternalToolSettings> tools)
        => tools.Where(t => File.Exists(t.ApplicationPath)).Select(t => new ExternalDiffTool(System.IO.Path.GetFileNameWithoutExtension(t.ApplicationPath), t.ApplicationPath,
            t.Arguments.Length > 0 ? t.Arguments : null, t.Arguments.Length > 0 ? t.Arguments : null));

    private static List<ExternalDiffTool> BuildCatalog()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string First(params string[] candidates) => candidates.FirstOrDefault(File.Exists) ?? "";

        // 例：C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\vsDiffMerge.exe
        var vs = new[] { Path.Combine(pf, "Microsoft Visual Studio"), Path.Combine(pf86, "Microsoft Visual Studio") }
            .Where(Directory.Exists)
            .SelectMany(d => VisualStudioFiles(d, Path.Combine("Common7", "IDE", "CommonExtensions", "Microsoft", "TeamFoundation", "Team Explorer", "vsDiffMerge.exe")))
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
        const string Code = "-n --wait --merge \"$REMOTE\" \"$LOCAL\" \"$BASE\" \"$MERGED\"";
        const string CodeDiff = "--diff --wait \"$REMOTE\" \"$LOCAL\"";
        return
        [
            new("Visual Studio", vs, "\"$REMOTE\" \"$LOCAL\" /t", "\"$REMOTE\" \"$LOCAL\" \"$BASE\" \"$MERGED\" /m"),
            new("VS Code", First(Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"), Path.Combine(pf, "Microsoft VS Code", "Code.exe")), CodeDiff, Code),
            new("WinMerge", First(Path.Combine(pf, "WinMerge", "WinMergeU.exe"), Path.Combine(pf86, "WinMerge", "WinMergeU.exe"), Path.Combine(local, "Programs", "WinMerge", "WinMergeU.exe")),
                "-u -e \"$REMOTE\" \"$LOCAL\"", "-u -e \"$REMOTE\" \"$BASE\" \"$LOCAL\" -o \"$MERGED\""),
            new("Araxis Merge", First(Path.Combine(pf, "Araxis", "Araxis Merge", "Compare.exe")), "\"$REMOTE\" \"$LOCAL\"", "-wait -merge -3 -a1 \"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\""),
            new("Beyond Compare", First(Path.Combine(pf, "Beyond Compare 5", "BComp.exe"), Path.Combine(pf, "Beyond Compare 4", "BComp.exe"), Path.Combine(pf86, "Beyond Compare 4", "BComp.exe")),
                "\"$REMOTE\" \"$LOCAL\"", "\"$REMOTE\" \"$LOCAL\" \"$BASE\" \"$MERGED\""),
            new("CodeBuddy", First(Path.Combine(local, "Programs", "CodeBuddy", "CodeBuddy.exe")), CodeDiff, Code),
            new("Cursor", First(Path.Combine(local, "Programs", "cursor", "Cursor.exe")), CodeDiff, Code),
            new("KDiff3", First(Path.Combine(pf, "KDiff3", "bin", "kdiff3.exe"), Path.Combine(pf, "KDiff3", "kdiff3.exe")), "\"$REMOTE\" \"$LOCAL\"", "\"$REMOTE\" -b \"$BASE\" \"$LOCAL\" -o \"$MERGED\""),
            new("P4Merge", First(Path.Combine(pf, "Perforce", "p4merge.exe")), "\"$REMOTE\" \"$LOCAL\"", "\"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\""),
            // Unity の UnityYAMLMerge（マージ専用。Diff の一覧には出さない）
            new("YAMLMerge", "", null, "merge -p \"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\""),
        ];
    }

    // Visual Studio のフォルダは深いので、<版>\<エディション>\ の下の決まった場所だけを見る
    private static IEnumerable<string> VisualStudioFiles(string root, string relative)
    {
        IEnumerable<string> editions;
        try { editions = Directory.EnumerateDirectories(root).SelectMany(v => Directory.EnumerateDirectories(v)); }
        catch { yield break; }
        foreach (var e in editions)
        {
            var p = Path.Combine(e, relative);
            if (File.Exists(p)) yield return p;
        }
    }
}
