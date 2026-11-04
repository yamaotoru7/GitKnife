using GitKnife.Core;
using GitKnife.Core.Logging;

namespace GitKnife;

/// <summary>
/// 送り先のサーバーの種類。覚えていればそれを使い、無ければ初めて使うときに判定して settings.json（ServerKinds）に覚える。
/// 通信できなかったときは覚えずにホスト名で推測し、次の機会にもう一度判定する。
/// </summary>
internal static class ServerKinds
{
    public static async Task<ServerKind> ResolveAsync(string serverUrl, CancellationToken ct = default)
    {
        if (serverUrl.Trim().Length == 0) return ServerKind.Unknown;
        if (App.Settings.GetServerKind(serverUrl) is { } known) return known.OrGuess(serverUrl);
        var detected = await ServerProbe.DetectAsync(serverUrl, ct: ct);
        if (detected is not { } kind)
        {
            Log.Info($"Server type of {serverUrl}: not reachable, guessing {ServerProbe.Guess(serverUrl)} from the host name");
            return ServerProbe.Guess(serverUrl);
        }
        Log.Info($"Server type of {serverUrl}: {kind}");
        App.Settings.SetServerKind(serverUrl, kind);
        App.SaveSettings();
        return kind.OrGuess(serverUrl);
    }
}
