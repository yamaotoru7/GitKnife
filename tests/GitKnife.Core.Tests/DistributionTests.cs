namespace GitKnife.Core.Tests;

/// <summary>ダウンロード元（Zone.Identifier）から配布先を読む。</summary>
public class DistributionTests
{
    [Fact]
    public void GitHubUsesTheReleasePageInReferrer()
    {
        // GitHub は HostUrl がファイルの置き場で、リリースのページは ReferrerUrl
        var zone = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://github.com/owner/GitKnife/releases/tag/v0.1.10\r\n"
                 + "HostUrl=https://release-assets.githubusercontent.com/github-production-release-asset/1/2?sp=r&rscd=attachment\r\n";
        Assert.Equal(("https://github.com/", "owner/GitKnife"), Distribution.ParseZoneIdentifier(zone));
    }

    [Fact]
    public void ForgejoUsesHostOrReferrer()
    {
        Assert.Equal(("https://git.example.com/", "dev/gitknife"), Distribution.ParseZoneIdentifier(
            "HostUrl=https://git.example.com/dev/gitknife/releases/download/v0.1.10/GitKnife-0.1.10-setup.exe"));
        // 添付の URL（/attachments/…）しか無いときは、リリースのページ（ReferrerUrl）から
        Assert.Equal(("https://git.example.com/", "dev/gitknife"), Distribution.ParseZoneIdentifier(
            "ReferrerUrl=https://git.example.com/dev/gitknife/releases\nHostUrl=https://git.example.com/attachments/0c1d"));
        // サブパスに置いたサーバー
        Assert.Equal(("https://example.com/forge/", "dev/gitknife"), Distribution.ParseZoneIdentifier(
            "HostUrl=https://example.com/forge/dev/gitknife/releases/download/v1/x.exe"));
    }

    /// <summary>ユーザーが入れるリポジトリの URL（更新とバグ報告で共通）。</summary>
    [Theory]
    [InlineData("https://github.com/owner/GitKnife", "https://github.com/", "owner/GitKnife")]
    [InlineData(" https://github.com/owner/GitKnife.git/ ", "https://github.com/", "owner/GitKnife")]
    [InlineData("https://github.com/owner/GitKnife/releases/tag/v1", "https://github.com/", "owner/GitKnife")]
    [InlineData("https://git.example.com/dev/gitknife/issues", "https://git.example.com/", "dev/gitknife")]
    [InlineData("https://example.com/forge/dev/gitknife", "https://example.com/forge/", "dev/gitknife")]
    public void ParsesRepositoryUrl(string url, string server, string repo)
    {
        Assert.Equal((server, repo), Distribution.ParseRepositoryUrl(url));
        Assert.Equal(server + repo, Distribution.RepositoryUrl(server, repo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("github.com/owner/repo")]
    [InlineData("https://github.com/owner")]
    [InlineData("ftp://example.com/a/b")]
    public void RejectsBadRepositoryUrl(string url) => Assert.Null(Distribution.ParseRepositoryUrl(url));

    [Theory]
    [InlineData("")]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\n")]
    [InlineData("HostUrl=https://git.example.com/attachments/0c1d")]
    [InlineData("ReferrerUrl=https://github.com/\r\nHostUrl=https://release-assets.githubusercontent.com/a/b")]
    [InlineData("HostUrl=file:///C:/share/releases/x.exe")]
    public void UnknownSourceIsNull(string zone) => Assert.Null(Distribution.ParseZoneIdentifier(zone));
}
