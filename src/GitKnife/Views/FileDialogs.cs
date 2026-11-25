using System.IO;
using GitKnife.Core.Git;

namespace GitKnife.Views;

/// <summary>Windows のファイルの選択の窓（Open / Save）の補助。</summary>
public static class FileDialogs
{
    /// <summary>
    /// 窓の初めの場所に使えるフォルダ（前に使ったフォルダなど）。窓は Windows のフォルダしか開けないので SSH の先は使わない。
    /// UI のスレッドで呼ぶので、あるかどうかはドライブのフォルダだけ確かめる（ネットワークの先は確かめずに渡す。無ければ窓が既定の場所で開く）。
    /// </summary>
    public static string? ExistingLocalDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || SshPath.IsSshPath(directory)) return null;
        if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal)) return directory;
        return Directory.Exists(directory) ? directory : null;
    }
}
