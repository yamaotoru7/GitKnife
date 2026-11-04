using System.Text;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Git;

/// <summary>
/// GitKnife.AskPass.exe（SSH_ASKPASS）と本体の間の約束事。ssh は質問の文言を引数に付けて AskPass を起動し、標準出力の 1 行を答えにする。
/// AskPass は環境変数の名前付きパイプに繋ぎ、"GKASKPASS 1"、セッション、質問（Base64）の 3 行を送る。本体は "OK &lt;Base64&gt;" か "NO" を返す。
/// </summary>
public static class AskPassProtocol
{
    public const string PipeVariable = "GITKNIFE_ASKPASS_PIPE";
    public const string SessionVariable = "GITKNIFE_ASKPASS_SESSION";
    private const string Magic = "GKASKPASS 1";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>AskPass の側：質問を送って答えを受け取る（断られたら null）。</summary>
    public static string? Ask(Stream pipe, string session, string prompt)
    {
        using var w = new StreamWriter(pipe, Utf8, 1024, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
        w.WriteLine(Magic);
        w.WriteLine(session);
        w.WriteLine(Convert.ToBase64String(Utf8.GetBytes(prompt)));
        using var r = new StreamReader(pipe, Utf8, false, 1024, leaveOpen: true);
        var line = r.ReadLine();
        return line is not null && line.StartsWith("OK ", StringComparison.Ordinal) ? Utf8.GetString(Convert.FromBase64String(line[3..])) : null;
    }

    /// <summary>本体の側：質問を読む（形が違えば null）。</summary>
    public static async Task<(string Session, string Prompt)?> ReadRequestAsync(StreamReader reader, CancellationToken ct)
    {
        if (await reader.ReadLineAsync(ct).ConfigureAwait(false) != Magic) return null;
        var session = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        var prompt = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (session is null || prompt is null) return null;
        try { return (session, Utf8.GetString(Convert.FromBase64String(prompt))); }
        catch (FormatException) { return null; }
    }

    /// <summary>本体の側：答えを書く（null は断る）。</summary>
    public static string FormatAnswer(string? answer) => answer is null ? "NO" : "OK " + Convert.ToBase64String(Utf8.GetBytes(answer));
}

public enum AskPassKind
{
    /// <summary>ログインのパスワード（"user@host's password: "、keyboard-interactive の "Password: "）。</summary>
    Password,
    /// <summary>鍵のパスフレーズ（"Enter passphrase for key '…': "）。</summary>
    Passphrase,
    /// <summary>知らないホストの鍵の確認（"Are you sure you want to continue connecting (yes/no/[fingerprint])? "）。答えは yes か no。</summary>
    HostKey,
    /// <summary>そのほか（ワンタイムのコードなど）。覚えない。</summary>
    Other,
}

/// <summary>ssh の質問の種類。<see cref="Key"/> は答えを覚えるときの名前（パスワードは接続先ごと、パスフレーズは鍵のファイルごと）。</summary>
public sealed record AskPassQuestion(AskPassKind Kind, string Destination, string Prompt, string Key)
{
    /// <summary>覚えてよい答えか（パスワードとパスフレーズ）。</summary>
    public bool IsSecret => Kind is AskPassKind.Password or AskPassKind.Passphrase;
}

public static partial class AskPassClassifier
{
    [GeneratedRegex(@"passphrase for key '(?<key>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex PassphraseRegex();

    public static AskPassQuestion Classify(string destination, string prompt)
    {
        // 既知ホストの確認。yes/no 以外を返すと ssh は "Please type 'yes', 'no' or the fingerprint" を際限なく繰り返す
        if (prompt.Contains("(yes/no", StringComparison.OrdinalIgnoreCase) || prompt.Contains("Please type 'yes'", StringComparison.OrdinalIgnoreCase))
            return new(AskPassKind.HostKey, destination, prompt, "hostkey:" + destination);
        if (PassphraseRegex().Match(prompt) is { Success: true } m)
            return new(AskPassKind.Passphrase, destination, prompt, "ssh-key:" + m.Groups["key"].Value);
        if (prompt.Contains("password", StringComparison.OrdinalIgnoreCase))
            return new(AskPassKind.Password, destination, prompt, "ssh:" + destination);
        return new(AskPassKind.Other, destination, prompt, "other:" + destination + ":" + prompt);
    }
}
