using System.Diagnostics;

namespace GitKnife.Testing;

/// <summary>
/// 動作確認に使う使い捨てのテスト用リポジトリ。何度作っても同じ SHA になるように、
/// 作者・日時・内容を固定する。
/// GitKnife.Core.Tests がテストの前提リポジトリとしてリンクして使う。
/// </summary>
public static class SandboxBuilder
{
    private const string Author = "Sandbox User";
    private const string Email = "sandbox@example.com";

    /// <summary>dir の下に remote（bare）と basic と sublib（submodule 用）を作る。basic のパスを返す。quiet なら標準出力に書かない。</summary>
    public static string Create(string dir, bool quiet = false)
    {
        DeleteDirectory(dir);
        Directory.CreateDirectory(dir);

        var remote = Path.Combine(dir, "remote");
        var basic = Path.Combine(dir, "basic");

        // origin（bare）
        Directory.CreateDirectory(remote);
        Git(remote, "init --bare --initial-branch=main -q");

        // basic：main に 5 コミット、feature/login に 2 コミット、release/1.0 タグ、stash、競合ブランチ
        Directory.CreateDirectory(basic);
        Git(basic, "init --initial-branch=main -q");
        Git(basic, "config user.name \"" + Author + "\"");
        Git(basic, "config user.email " + Email);
        Git(basic, "config commit.gpgsign false");
        Git(basic, "config core.autocrlf false");

        var t = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(9));
        Write(basic, "README.md", "# Sandbox\n\n動作確認に使うリポジトリ。\n");
        Write(basic, ".gitignore", "bin/\nobj/\n");
        Commit(basic, "Initial commit", ref t);

        Write(basic, "src/app.cs", "class App\n{\n    static void Main() { }\n}\n");
        Commit(basic, "Add app skeleton", ref t);

        Write(basic, "src/app.cs", "class App\n{\n    static void Main()\n    {\n        System.Console.WriteLine(\"hello\");\n    }\n}\n");
        Commit(basic, "Print greeting\n\nThe first real feature. This commit has a description body\nso that the ↩ symbol appears in the commit list.", ref t);

        Git(basic, "tag -a v0.1 -m \"First tag\"", Env(t));
        t = t.AddHours(2);

        // feature/login（main から分岐）
        Git(basic, "switch -c feature/login -q");
        Write(basic, "src/login.cs", "class Login\n{\n    public bool Check(string u, string p) => u == \"admin\";\n}\n");
        Commit(basic, "Add login check", ref t);
        Write(basic, "src/login.cs", "class Login\n{\n    public bool Check(string u, string p) => u == \"admin\" && p.Length > 3;\n}\n");
        Commit(basic, "Require a password", ref t);

        // main を進める（グラフが分岐して見えるように）
        Git(basic, "switch main -q");
        Write(basic, "docs/usage.md", "# Usage\n\nRun the app.\n");
        Commit(basic, "Add usage doc", ref t);
        Write(basic, "src/app.cs", "class App\n{\n    static void Main()\n    {\n        System.Console.WriteLine(\"hello, world\");\n    }\n}\n");
        Commit(basic, "Greet the world", ref t);
        Git(basic, "tag v0.2");

        // 競合用ブランチ：main と同じ行を別内容で変える
        Git(basic, "switch -c bugfix/greeting -q HEAD~1");
        Write(basic, "src/app.cs", "class App\n{\n    static void Main()\n    {\n        System.Console.WriteLine(\"Hello!\");\n    }\n}\n");
        Commit(basic, "Capitalize greeting", ref t);
        Git(basic, "switch main -q");

        // リモートへ push。main は origin より 1 つ進んだ状態（ahead=1）、feature/login は未 push
        Git(basic, "remote add origin \"" + remote.Replace('\\', '/') + "\"");
        Git(basic, "push -q -u origin main:main bugfix/greeting:bugfix/greeting --tags");
        // origin 側だけに別のコミットを置いて behind=1 にする（別クローンから push）
        var helper = Path.Combine(dir, "helper");
        Git(dir, "clone -q \"" + remote.Replace('\\', '/') + "\" helper");
        Git(helper, "config user.name \"Other Dev\"");
        Git(helper, "config user.email other@example.com");
        Write(helper, "CHANGELOG.md", "# Changelog\n\n- 0.2: greet the world\n");
        Commit(helper, "Add changelog", ref t, "Other Dev", "other@example.com");
        Git(helper, "push -q origin main");
        DeleteDirectory(helper);
        Git(basic, "fetch -q origin");
        Write(basic, "docs/usage.md", "# Usage\n\nRun the app.\n\n## Options\n\nNone yet.\n");
        Commit(basic, "Document options", ref t);

        // stash と作業ツリーの変更（unstaged / staged / untracked を 1 つずつ）
        Write(basic, "src/app.cs", File.ReadAllText(Path.Combine(basic, "src/app.cs")).Replace("hello, world", "hello, stash"));
        Git(basic, "stash push -q -m \"WIP: greeting experiment\"", Env(t));
        Write(basic, "README.md", "# Sandbox\n\n動作確認に使うリポジトリ。\n\n（unstaged の変更）\n");
        Write(basic, "docs/todo.md", "- write the usage notes\n");
        Git(basic, "add docs/todo.md");
        Write(basic, "notes.txt", "untracked file\n");

        // submodule に足す用のリポジトリ（basic には入れない）
        var sublib = Path.Combine(dir, "sublib");
        Directory.CreateDirectory(sublib);
        Git(sublib, "init --initial-branch=main -q");
        Git(sublib, "config user.name \"" + Author + "\"");
        Git(sublib, "config user.email " + Email);
        Write(sublib, "lib.txt", "lib\n");
        Commit(sublib, "Add lib", ref t);
        Write(sublib, "lib.txt", "lib\nv2\n");
        Commit(sublib, "Update lib", ref t);

        if (!quiet)
        {
            Console.WriteLine("sandbox: " + basic);
            Console.WriteLine(Git(basic, "log --oneline --graph --all --decorate"));
            Console.WriteLine(Git(basic, "status --short"));
        }
        return basic;
    }

    /// <summary>git の .git/objects は読み取り専用なので、属性を外してから消す。</summary>
    public static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    private static void Commit(string repo, string message, ref DateTimeOffset t, string? name = null, string? email = null)
    {
        Git(repo, "add -A");
        var env = Env(t, name, email);
        Git(repo, "commit -q -F -", env, stdin: message);
        t = t.AddMinutes(37);
    }

    private static Dictionary<string, string> Env(DateTimeOffset t, string? name = null, string? email = null) => new()
    {
        ["GIT_AUTHOR_NAME"] = name ?? Author,
        ["GIT_AUTHOR_EMAIL"] = email ?? Email,
        ["GIT_COMMITTER_NAME"] = name ?? Author,
        ["GIT_COMMITTER_EMAIL"] = email ?? Email,
        ["GIT_AUTHOR_DATE"] = t.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        ["GIT_COMMITTER_DATE"] = t.ToString("yyyy-MM-ddTHH:mm:sszzz"),
    };

    private static void Write(string repo, string relative, string content)
    {
        var path = Path.Combine(repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
    }

    public static string Git(string cwd, string args, Dictionary<string, string>? env = null, string? stdin = null)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        if (stdin is not null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {args} failed ({p.ExitCode}) in {cwd}:\n{stderr}");
        return stdout;
    }
}
