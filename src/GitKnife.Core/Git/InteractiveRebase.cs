using System.Text;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Git;

/// <summary>Interactive Rebase の 1 手（UI で選んだ動作と対象のコミット）。</summary>
public sealed record RebasePlanStep(string Action, string Sha);

/// <summary>
/// Interactive Rebase。git の起動：`-c core.commentChar=^ -c rebase.instructionFormat=#!_%H -c rebase.abbreviateCommands=true
/// -c sequence.editor=<RI> -c core.editor=<RI> rebase -i --autosquash --update-refs <base>`。
///
/// UI で先に計画を作り、
/// GitKnife.RI.exe（sequence.editor）が git の作った todo に計画を当てはめる（パイプは使わない）。
/// todo の各行の末尾の "#!_<sha>"（instructionFormat）で、どのコミットの行かが分かる。
/// </summary>
public static partial class InteractiveRebase
{
    /// <summary>計画で使う動作（todo の省略形も受け付ける）。</summary>
    public static readonly IReadOnlyList<string> Actions = ["pick", "reword", "edit", "squash", "fixup", "drop"];

    [GeneratedRegex(@"#!_([0-9a-f]{40})")]
    private static partial Regex ShaMarker();

    /// <summary>
    /// git の todo に計画を当てはめる。コミットの行（#!_sha のある行）は計画の順に並べ替えて動作を置き換え、
    /// 計画に無いコミットの行は drop にする。それ以外の行（update-ref、label、空行、コメント）は元の位置に残す。
    /// </summary>
    public static string ApplyPlan(string todo, IReadOnlyList<RebasePlanStep> plan)
    {
        var lines = todo.Replace("\r\n", "\n").Split('\n');
        // "Update dependent branches" がオフ：常に --update-refs で起動するので、git の入れた update-ref の行をここで消す
        if (plan.Contains(NoUpdateRefs))
        {
            lines = lines.Where(l => !l.TrimStart().StartsWith("update-ref ", StringComparison.Ordinal)).ToArray();
            plan = plan.Where(p => p != NoUpdateRefs).ToList();
        }
        var commitLines = new Dictionary<string, string>();
        var slots = new List<int>();   // コミットの行があった位置
        for (var i = 0; i < lines.Length; i++)
        {
            var m = ShaMarker().Match(lines[i]);
            if (!m.Success || lines[i].TrimStart().StartsWith('^') || lines[i].TrimStart().StartsWith('#')) continue;
            commitLines[m.Groups[1].Value] = lines[i];
            slots.Add(i);
        }

        // 計画の順に並べる。計画に無いコミットは落とす（drop と同じ）
        var ordered = new List<string>();
        foreach (var step in plan)
        {
            var key = commitLines.Keys.FirstOrDefault(k => k.StartsWith(step.Sha, StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var line = commitLines[key];
            var rest = line.TrimStart();
            var space = rest.IndexOf(' ');
            ordered.Add(space < 0 ? line : $"{step.Action} {rest[(space + 1)..]}");
        }

        var sb = new StringBuilder();
        var slot = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (slot < slots.Count && slots[slot] == i)
            {
                // コミットの行の位置には、計画の順にコミットを詰める（余った位置は消す）
                if (slot < ordered.Count) sb.Append(ordered[slot]).Append('\n');
                if (slot == slots.Count - 1)
                    for (var k = slots.Count; k < ordered.Count; k++) sb.Append(ordered[k]).Append('\n');
                slot++;
                continue;
            }
            sb.Append(lines[i]).Append('\n');
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// reword の新しいメッセージを探す：rebase-merge/done の最後の行の "#!_sha" から、messagesDir/&lt;sha&gt;.txt を読む。無ければ null（そのまま）。
    /// </summary>
    public static string? FindMessage(string gitDirectory, string messagesDirectory)
    {
        var done = Path.Combine(gitDirectory, "rebase-merge", "done");
        if (!HostFile.Exists(done)) return null;
        var last = HostFile.ReadAllLines(done).LastOrDefault(l => l.Trim().Length > 0);
        if (last is null) return null;
        var m = ShaMarker().Match(last);
        if (!m.Success) return null;
        var file = Path.Combine(messagesDirectory, m.Groups[1].Value + ".txt");
        return HostFile.Exists(file) ? HostFile.ReadAllText(file) : null;
    }

    /// <summary>計画に入れる印：update-ref の行を残さない（"Update dependent branches" がオフ）。</summary>
    public static readonly RebasePlanStep NoUpdateRefs = new("update-refs", "off");

    /// <summary>計画ファイル（1 行 "action sha"。先頭が "cancel" なら中止）。</summary>
    public static string FormatPlan(IEnumerable<RebasePlanStep> plan) => string.Join("\n", plan.Select(p => $"{p.Action} {p.Sha}")) + "\n";

    public static IReadOnlyList<RebasePlanStep>? ParsePlan(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].Trim() == "cancel") return null;
        return lines.Select(l => l.Trim().Split(' ', 2)).Where(p => p.Length == 2).Select(p => new RebasePlanStep(p[0], p[1])).ToList();
    }

    private static string Q(string p) => "\"" + p.Replace('\\', '/') + "\"";

    /// <summary>
    /// RI の起動。WSL の git は editor を WSL の sh で起動するので、RI は /mnt/c/… の形で呼び（Windows の exe は WSL から動く）、
    /// git が付けるファイルのパス（/home/…）を Windows の形に直せるよう --wsl &lt;distro&gt; を先に付ける（T1305）。
    /// 計画ファイルとメッセージのフォルダは RI（Windows）が読むので Windows の形のまま。
    /// </summary>
    private static string Ri(string riPath, GitHost? host) =>
        host is WslGitHost wsl ? $"{Q(wsl.ToHostPath(riPath))} --wsl {Q(wsl.Distribution)}" : Q(riPath);
    private static string SequenceEditor(string riPath, string planFile, GitHost? host) =>
        host is SshGitHost ssh ? $"sh {SshGitHost.Quote(ssh.ToHostPath(Path.GetDirectoryName(planFile)!).TrimEnd('/') + "/seq.sh")}" : $"{Ri(riPath, host)} --plan {Q(planFile)}";
    private static string MessageEditor(string riPath, string messagesDir, GitHost? host) =>
        host is SshGitHost ssh ? $"sh {SshGitHost.Quote(ssh.ToHostPath(Path.GetDirectoryName(messagesDir)!).TrimEnd('/') + "/msg.sh")}" : $"{Ri(riPath, host)} --messages {Q(messagesDir)}";

    // ---- SSH の接続先：Windows の RI.exe は呼べないので、同じ処理をシェルのスクリプトにして作業フォルダ（plan.txt と messages/ の横）に置く ----

    /// <summary>
    /// <see cref="ApplyPlan"/> と同じ処理の awk（PLAN は計画の行を ";" でつないだもの）。結果が C# と同じことは GitHostTests で確かめる。
    /// </summary>
    public const string ApplyPlanAwk = """
        BEGIN {
          n = split(PLAN, P, ";"); np = 0; noupd = 0
          for (i = 1; i <= n; i++) {
            if (P[i] == "") continue
            split(P[i], a, " ")
            if (a[1] == "update-refs" && a[2] == "off") { noupd = 1; continue }
            np++; act[np] = a[1]; sha[np] = a[2]
          }
        }
        { L[NR] = $0 }
        END {
          nl = 0
          for (i = 1; i <= NR; i++) {
            t = L[i]; sub(/^[ \t]+/, "", t)
            if (noupd && t ~ /^update-ref /) continue
            nl++; M[nl] = L[i]
          }
          while (nl > 0 && M[nl] == "") nl--
          ns = 0
          for (i = 1; i <= nl; i++) {
            t = M[i]; sub(/^[ \t]+/, "", t)
            if (substr(t, 1, 1) == "#" || substr(t, 1, 1) == "^") continue
            if (match(M[i], /#!_[0-9a-f]+/)) { s = substr(M[i], RSTART + 3, RLENGTH - 3); if (length(s) != 40) continue; C[s] = M[i]; ns++; S[ns] = i }
          }
          no = 0
          for (k = 1; k <= np; k++) {
            found = ""
            for (s in C) if (index(tolower(s), tolower(sha[k])) == 1) { found = s; break }
            if (found == "") continue
            line = C[found]; t = line; sub(/^[ \t]+/, "", t); sp = index(t, " ")
            no++; O[no] = (sp == 0) ? line : act[k] " " substr(t, sp + 1)
          }
          slot = 1
          for (i = 1; i <= nl; i++) {
            if (slot <= ns && S[slot] == i) {
              if (slot <= no) print O[slot]
              if (slot == ns) for (k = ns + 1; k <= no; k++) print O[k]
              slot++
              continue
            }
            print M[i]
          }
        }
        """;

    /// <summary>sequence.editor：計画が "cancel" なら 1 で終わる（git が rebase をやめる）。それ以外は todo に計画を当てはめる。</summary>
    public const string SequenceScript = """
        #!/bin/sh
        d=$(dirname "$0")
        if head -n 1 "$d/plan.txt" | grep -q '^cancel'; then exit 1; fi
        plan=$(tr '\n' ';' < "$d/plan.txt")
        awk -v PLAN="$plan" -f "$d/apply.awk" "$1" > "$1.gitknife" && mv "$1.gitknife" "$1"
        """;

    /// <summary>core.editor：<see cref="FindMessage"/> と同じく done の最後の行の "#!_sha" から messages/&lt;sha&gt;.txt に差し替える。無ければそのまま。</summary>
    public const string MessageScript = """
        #!/bin/sh
        d=$(dirname "$0")
        done=$(git rev-parse --git-path rebase-merge/done 2>/dev/null)
        [ -f "$done" ] || exit 0
        last=$(grep -v '^[[:space:]]*$' "$done" | tail -n 1)
        sha=$(printf '%s\n' "$last" | grep -o '#!_[0-9a-f]*' | head -n 1 | cut -c4-)
        if [ -n "$sha" ] && [ -f "$d/messages/$sha.txt" ]; then cp "$d/messages/$sha.txt" "$1"; fi
        exit 0
        """;

    /// <summary>SSH の接続先の作業フォルダ（plan.txt の横）に apply.awk、seq.sh、msg.sh を書く。dir は \\gitknife-ssh\… の形。</summary>
    public static async Task WriteRemoteScriptsAsync(string dir, CancellationToken ct = default)
    {
        static string Lf(string s) => s.Replace("\r\n", "\n") + "\n";
        await HostFile.WriteAllTextAsync(Path.Combine(dir, "apply.awk"), Lf(ApplyPlanAwk), ct).ConfigureAwait(false);
        await HostFile.WriteAllTextAsync(Path.Combine(dir, "seq.sh"), Lf(SequenceScript), ct).ConfigureAwait(false);
        await HostFile.WriteAllTextAsync(Path.Combine(dir, "msg.sh"), Lf(MessageScript), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 環境変数（GIT_EDITOR / GIT_SEQUENCE_EDITOR は -c より強いので、利用者の環境に入っていても効くように同じ値を入れる）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Environment(string riPath, string planFile, string messagesDir, GitHost? host = null) => new Dictionary<string, string>
    {
        ["GIT_SEQUENCE_EDITOR"] = SequenceEditor(riPath, planFile, host),
        ["GIT_EDITOR"] = MessageEditor(riPath, messagesDir, host),
    };

    /// <summary>git の起動引数。riPath は GitKnife.RI.exe、planFile と messagesDir は GitKnife が書いた一時ファイル・フォルダ。</summary>
    public static string[] Arguments(string riPath, string planFile, string messagesDir, string baseSha, GitHost? host = null)
    {
        var args = new List<string>
        {
            "-c", "core.commentChar=^",
            "-c", "rebase.instructionFormat=#!_%H",
            "-c", "rebase.abbreviateCommands=true",
            "-c", $"sequence.editor={SequenceEditor(riPath, planFile, host)}",
            "-c", $"core.editor={MessageEditor(riPath, messagesDir, host)}",
            "rebase", "-i", "--autosquash",
        };
        args.Add("--update-refs");   // 常に付ける（チェックボックスの状態は計画の NoUpdateRefs で）
        args.Add(baseSha);
        return [.. args];
    }
}
