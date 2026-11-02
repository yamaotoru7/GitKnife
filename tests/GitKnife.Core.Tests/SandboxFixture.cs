using GitKnife.Core.Git;
using GitKnife.Testing;

namespace GitKnife.Core.Tests;

/// <summary>
/// テスト用の basic リポジトリを一時フォルダに 1 回だけ作る（読み取り専用のテストで共有する）。
/// 書き込むテストは <see cref="CreateFresh"/> で自分専用のコピーを作る。
/// </summary>
public sealed class SandboxFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", "sandbox-" + Guid.NewGuid().ToString("N")[..8]);
    public string Basic { get; }
    public GitProcess Git { get; } = new(GitExecutable.Resolve(null));

    public SandboxFixture() => Basic = SandboxBuilder.Create(Root, quiet: true);

    public static string CreateFresh(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", "sandbox-" + Guid.NewGuid().ToString("N")[..8]);
        return SandboxBuilder.Create(root, quiet: true);
    }

    public void Dispose()
    {
        try { SandboxBuilder.DeleteDirectory(Root); } catch { }
    }
}

[CollectionDefinition("sandbox")]
public sealed class SandboxCollection : ICollectionFixture<SandboxFixture>;
