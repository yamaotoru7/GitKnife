using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

public class CredentialDiagnosticsTests
{
    // 2026-10-28 に別の PC の push で出た stderr
    private const string PushFailure = "Pushing to http://gitserver:5555/user/repo.git\nfatal: could not read Username for 'http://gitserver:5555': terminal prompts disabled\n";

    [Fact]
    public void DetectsAuthFailureAndUrl()
    {
        Assert.True(CredentialDiagnostics.LooksLikeAuthFailure(PushFailure));
        Assert.Equal("http://gitserver:5555", CredentialDiagnostics.FindUrl(PushFailure));
    }

    [Theory]
    [InlineData("remote: HTTP Basic: Access denied\nfatal: Authentication failed for 'https://user@example.com/a/b.git/'\n", "https://example.com")]
    [InlineData("Pushing to https://example.com:8443/a.git\nfatal: could not read Password for 'https://me@example.com:8443': terminal prompts disabled\n", "https://example.com:8443")]
    public void UrlDropsUserAndPath(string stderr, string expected)
    {
        Assert.True(CredentialDiagnostics.LooksLikeAuthFailure(stderr));
        Assert.Equal(expected, CredentialDiagnostics.FindUrl(stderr));
    }

    [Fact]
    public void IgnoresOtherFailures()
    {
        Assert.False(CredentialDiagnostics.LooksLikeAuthFailure("error: failed to push some refs to 'http://example.com/a.git'\nhint: Updates were rejected because the tip of your current branch is behind\n"));
    }
}
