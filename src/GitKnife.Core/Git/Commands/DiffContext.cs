namespace GitKnife.Core.Git.Commands;

/// <summary>
/// diff の文脈の行数と、空白を無視するか（diff の上の IgnoreWhitespacesToggleButton。設定 DiffIgnoreWhitespaces）。
/// `--unified=N` の後ろに `--ignore-all-space` を付ける。int から暗黙に変換できる（文脈の行数だけ）。
/// </summary>
public readonly record struct DiffContext(int Lines, bool IgnoreWhitespace = false)
{
    public static implicit operator DiffContext(int lines) => new(lines);

    public string[] Args => IgnoreWhitespace ? [$"--unified={Lines}", "--ignore-all-space"] : [$"--unified={Lines}"];
}
