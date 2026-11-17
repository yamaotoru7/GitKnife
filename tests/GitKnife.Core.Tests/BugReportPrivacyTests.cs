using GitKnife.Core.Reporting;

namespace GitKnife.Core.Tests;

/// <summary>バグレポートで人や PC が分かる部分を伏せる（#21）。</summary>
public class BugReportPrivacyTests
{
    private static BugReportPrivacy Sample() => new(
        [@"E:\Proj\ALLNet\security_check", @"D:\Work\other"],
        [
            (@"C:\Users\taro_y\AppData\Local", "%LOCALAPPDATA%"),
            (@"C:\Users\taro_y", "%USERPROFILE%"),
        ],
        [("taro_y", BugReportPrivacy.UserToken), ("DESK-01", BugReportPrivacy.MachineToken)]);

    [Fact]
    public void MasksGitPathUnderUserFolders()
    {
        var p = Sample();
        Assert.Equal(@"%LOCALAPPDATA%\Programs\GitKnife\git\cmd\git.exe", p.Mask(@"C:\Users\taro_y\AppData\Local\Programs\GitKnife\git\cmd\git.exe"));
        Assert.Equal(@"%USERPROFILE%\.ssh\known_hosts", p.Mask(@"c:\users\TARO_Y\.ssh\known_hosts"));
        // 8.3 形式の短い名前や / 区切りも
        Assert.Equal(@"C:\Users\<user>\AppData\Local\Temp\x", p.Mask(@"C:\Users\TARO_Y~1\AppData\Local\Temp\x"));
        Assert.Equal("%USERPROFILE%/AppData", p.Mask("/c/Users/taro_y/AppData"));
        Assert.Equal("/home/<user>/repo", p.Mask("/home/hanako/repo"));
        Assert.Equal("find '/disk/home/<user>' -maxdepth 1", p.Mask("find '/disk/home/hanako' -maxdepth 1"));
        // システムの git はそのまま
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", p.Mask(@"C:\Program Files\Git\cmd\git.exe"));
    }

    [Fact]
    public void MasksRepositoryPathsInEveryForm()
    {
        var p = Sample();
        Assert.Equal(@"Opening <repository>", p.Mask(@"Opening E:\Proj\ALLNet\security_check"));
        Assert.Equal(@"cd <repository>/src && <repository 2>\a.txt", p.Mask(@"cd e:/proj/allnet/security_check/src && D:\Work\other\a.txt"));
        Assert.Equal("cwd=<repository>/src", p.Mask("cwd=/e/Proj/ALLNet/security_check/src"));
    }

    [Fact]
    public void MasksNamesEmailsAndUrlCredentials()
    {
        var p = Sample();
        Assert.Equal(@"user CORP\<user> on <machine>", p.Mask(@"user CORP\taro_y on DESK-01"));
        Assert.Equal("Author: <email>", p.Mask("Author: taro.yamada@example.co.jp"));
        Assert.Equal("push https://<user>@git.example.com/<path>", p.Mask("push https://taro:ghp_secret@git.example.com/a/b.git"));
        // URL のパス（アカウントや組織の名前）はホストまで残して伏せる
        Assert.Equal("Fetching 'http://host:5555/<path>' (origin)", p.Mask("Fetching 'http://host:5555/taro.yamada/lt.git/' (origin)"));
        Assert.Equal("git@github.com:<path> ok", p.Mask("git@github.com:owner/repo.git ok"));
        Assert.Equal("see https://git.example.com)", p.Mask("see https://git.example.com)"));
        // 資格情報の診断のアカウント名は、ほかの場所でも伏せる
        Assert.Equal("    gh:host.example.com:<user>  user=<user>  type=1", p.Mask("    gh:host.example.com:hanako-s  user=hanako-s  type=1"));
        // 名前の一部になっている語は伏せない
        Assert.Equal("taro_yx", p.Mask("taro_yx"));
    }

    /// <summary>表の git は接続先で使う git（WSL・SSH はその場所の git で、名前は出さない）。</summary>
    [Fact]
    public void GitDescriptionFollowsHost()
    {
        const string local = @"C:\Program Files\Git\cmd\git.exe";
        Assert.Equal(local, BugReportPrivacy.GitDescription(GitKnife.Core.Git.GitHost.Local, local));
        Assert.Equal("git in WSL", BugReportPrivacy.GitDescription(new GitKnife.Core.Git.WslGitHost("Ubuntu"), local));
        Assert.Equal("git on SSH host (/opt/git/bin/git)", BugReportPrivacy.GitDescription(new GitKnife.Core.Git.SshGitHost("build", "taro@build.example.com", "/opt/git/bin/git"), local));
        Assert.Equal("git on SSH host (git)", BugReportPrivacy.GitDescription(new GitKnife.Core.Git.SshGitHost("build", "build"), local));
    }

    [Fact]
    public void RepositoryKindHidesPath()
    {
        Assert.Equal("Local", BugReportPrivacy.RepositoryKind(@"E:\Proj\ALLNet\security_check"));
        Assert.Equal("WSL", BugReportPrivacy.RepositoryKind(@"\\wsl.localhost\Ubuntu\home\taro\repo"));
    }
}
