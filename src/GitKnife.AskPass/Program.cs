// GitKnife.AskPass.exe：ssh の SSH_ASKPASS。
//   ssh が「GitKnife.AskPass.exe <質問の文言>」で起動し、標準出力の 1 行を答えにする（パスワード、パスフレーズ、既知ホストの yes / no）。
//   質問は GITKNIFE_ASKPASS_PIPE の名前付きパイプで本体に送り、本体が画面で聞くか覚えた答えを返す。
//   断られたら何も出さずに 1 で終わる（ssh はその認証をあきらめる）。
using System.IO.Pipes;
using System.Text;
using GitKnife.Core.Git;

var pipe = Environment.GetEnvironmentVariable(AskPassProtocol.PipeVariable);
var session = Environment.GetEnvironmentVariable(AskPassProtocol.SessionVariable);
if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(session))
{
    Console.Error.WriteLine("GitKnife.AskPass: this program is started by GitKnife.");
    return 2;
}

try
{
    using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
    client.Connect(10_000);
    var answer = AskPassProtocol.Ask(client, session, string.Join(' ', args));
    if (answer is null) return 1;
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(new UTF8Encoding(false).GetBytes(answer + "\n"));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"GitKnife.AskPass: {ex.Message}");
    return 1;
}
