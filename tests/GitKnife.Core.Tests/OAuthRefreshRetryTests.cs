using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

public class OAuthRefreshRetryTests
{
    // 2026-11-04 に http のリモートへの push で出た stderr（しばらく使わず、GCM のアクセストークンが切れていた）
    private const string ExpiredToken = "Pushing to http://gitserver:5555/user/repo.git\nremote: Verify\nfatal: Authentication failed for 'http://gitserver:5555/user/repo.git/'\n";

    [Fact]
    public void FindsOriginOfHttpAuthFailure()
    {
        Assert.Equal("http://gitserver:5555", OAuthRefreshRetry.FindFailedOrigin(ExpiredToken));
        Assert.Equal("https://example.com", OAuthRefreshRetry.FindFailedOrigin("fatal: Authentication failed for 'https://me@example.com/a/b.git/'\n"));
    }

    [Theory]
    [InlineData("fatal: could not read Username for 'http://gitserver:5555': terminal prompts disabled\n")]
    [InlineData("git@example.com: Permission denied (publickey).\nfatal: Could not read from remote repository.\n")]
    [InlineData("error: failed to push some refs to 'http://gitserver:5555/user/repo.git'\n")]
    public void IgnoresOtherFailures(string stderr)
    {
        Assert.Null(OAuthRefreshRetry.FindFailedOrigin(stderr));
        Assert.False(OAuthRefreshRetry.ShouldRetry(stderr, out _));
    }

    [Theory]
    [InlineData("http://gitserver:5555", "git:http://refresh_token.gitserver:5555")]
    [InlineData("https://example.com", "git:https://refresh_token.example.com")]
    public void RefreshTokenTargetMatchesCredentialManagerName(string origin, string expected) =>
        Assert.Equal(expected, OAuthRefreshRetry.RefreshTokenTarget(origin));

    [Fact]
    public void DoesNotRetryWithoutStoredRefreshToken() =>
        Assert.False(OAuthRefreshRetry.ShouldRetry("fatal: Authentication failed for 'https://no-such-host.gitknife.invalid/a.git/'\n", out _));
}
