namespace GitKnife.Core.Diff;

/// <summary>
/// submodule の diff（`diff --submodule=short`）の "Subproject commit &lt;sha&gt;" の行から、古いコミットと新しいコミットを取り出す。
/// 作業ツリーに変更がある submodule は新しい側が "&lt;sha&gt;-dirty" になる。足したばかりなら Old が null、消したなら New が null。
/// </summary>
public sealed record SubmoduleChange(string? Old, string? New, bool Dirty)
{
    private const string Prefix = "Subproject commit ";

    public static SubmoduleChange? TryParse(FileDiff diff)
    {
        string? oldSha = null, newSha = null;
        var dirty = false;
        var found = false;
        foreach (var line in diff.Hunks.SelectMany(h => h.Lines))
        {
            if (!line.Text.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var sha = line.Text[Prefix.Length..].Trim();
            var isDirty = sha.EndsWith("-dirty", StringComparison.Ordinal);
            if (isDirty) sha = sha[..^"-dirty".Length];
            found = true;
            switch (line.Kind)
            {
                case DiffLineKind.Removed: oldSha = sha; break;
                case DiffLineKind.Added: newSha = sha; dirty |= isDirty; break;
                default: oldSha ??= sha; newSha ??= sha; dirty |= isDirty; break;
            }
        }
        return found ? new SubmoduleChange(oldSha, newSha, dirty) : null;
    }
}
