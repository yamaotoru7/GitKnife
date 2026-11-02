using GitKnife.Core.Git;

// GitKnife.RI：Interactive Rebase のときに git が起動するエディタ役。
//   sequence.editor：GitKnife.RI.exe --plan <計画ファイル> <git-rebase-todo>
//       → git の todo に GitKnife が UI で作った計画を当てはめる。計画が "cancel" なら終了コード 1（git が rebase をやめる）
//   core.editor：    GitKnife.RI.exe --messages <フォルダ> <COMMIT_EDITMSG>
//   WSL の git から： GitKnife.RI.exe --wsl <distro> --plan … <todo の Linux のパス>（Linux のパスを UNC に直してから同じ処理）
//       → reword したコミットなら、GitKnife が書いた新しいメッセージに差し替える。それ以外はそのまま
// 詳しくは GitKnife.Core.Git.InteractiveRebase。

try
{
    // WSL の git から呼ばれたとき（--wsl <distro> が先に付く）：git が付けるファイルのパス（/home/…）を \\wsl.localhost\… に直す
    if (args.Length >= 2 && args[0] == "--wsl")
    {
        var host = new WslGitHost(args[1]);
        args = [.. args[2..^1], host.ToLocalPath(args[^1])];
    }
    if (args.Length == 3 && args[0] == "--plan")
    {
        var plan = InteractiveRebase.ParsePlan(File.ReadAllText(args[1]));
        if (plan is null) return 1;
        var todo = args[2];
        File.WriteAllText(todo, InteractiveRebase.ApplyPlan(File.ReadAllText(todo), plan));
        return 0;
    }
    if (args.Length == 3 && args[0] == "--messages")
    {
        var file = Path.GetFullPath(args[2]);
        var gitDir = Path.GetDirectoryName(file)!;
        if (InteractiveRebase.FindMessage(gitDir, args[1]) is { } message) File.WriteAllText(file, message);
        return 0;
    }
    Console.Error.WriteLine("usage: GitKnife.RI --plan <plan> <todo> | --messages <dir> <file>");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"GitKnife.RI: {ex.Message}");
    return 1;
}
