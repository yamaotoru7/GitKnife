using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Settings;

/// <summary>カスタムコマンドのファイルの場所（並びもこの順。）。</summary>
public enum CustomCommandSource { Global, Shared, Local }

/// <summary>ref の種類（`refTargets` の `localbranch` / `remotebranch` / `tag`）。</summary>
public enum CustomCommandRefKind { LocalBranch, RemoteBranch, Tag }

/// <summary>動作（`type`：`process` / `sh` / `url` / `cancel`）。</summary>
public sealed record CustomCommandAction(string Type, string? Path = null, string? Args = null, string? Script = null, string? Url = null,
    bool ShowOutput = false, bool WaitForExit = false)
{
    internal static CustomCommandAction? Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        return new CustomCommandAction(Str(o, "type") ?? "", Str(o, "path"), Str(o, "args"), Str(o, "script"), Str(o, "url"),
            Bool(o, "showOutput"), Bool(o, "waitForExit"));
    }

    internal static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    internal static bool Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>UI のコントロール（`textBox`（generic / filepath）、`dropdown`（references）、`checkBox`）。</summary>
public sealed record CustomCommandControl(string Type, string Title)
{
    public string TextBoxType { get; init; } = "generic";
    public string Text { get; init; } = "";
    public string Placeholder { get; init; } = "";
    /// <summary>File Path のダイアログ：`saveFile` / `openFile` / `openDirectory`。</summary>
    public string DialogType { get; init; } = "saveFile";
    public string DefaultDirectory { get; init; } = "";
    public string Filename { get; init; } = "";
    /// <summary>Branch Selector の絞り込み（ref の前方一致。空白区切りで複数）。</summary>
    public string Filter { get; init; } = "";
    public string CheckedValue { get; init; } = "";
    public string UncheckedValue { get; init; } = "";
    public bool DefaultValue { get; init; }

    /// <summary>`$1{text}` の `text` の部分（値の種類）。</summary>
    public string ValueKind => Type switch
    {
        "textBox" when TextBoxType == "filepath" => "path",
        "textBox" => "text",
        "dropdown" => "ref",
        _ => "value",
    };

    /// <summary>Branch Selector に出す ref か（filter が空ならすべて）。</summary>
    public bool MatchesFilter(string canonicalName)
    {
        var prefixes = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return prefixes.Length == 0 || prefixes.Any(p => canonicalName == p || canonicalName.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal));
    }

    internal static CustomCommandControl? Parse(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        string S(string key) => CustomCommandAction.Str(o, key) ?? "";
        return new CustomCommandControl(S("type"), S("title"))
        {
            TextBoxType = CustomCommandAction.Str(o, "textBoxType") ?? "generic",
            Text = S("text"), Placeholder = S("placeholder"),
            DialogType = CustomCommandAction.Str(o, "dialogType") ?? "saveFile", DefaultDirectory = S("defaultDirectory"), Filename = S("filename"),
            Filter = S("filter"),
            CheckedValue = S("checkedValue"), UncheckedValue = S("uncheckedValue"), DefaultValue = CustomCommandAction.Bool(o, "defaultValue"),
        };
    }
}

public sealed record CustomCommandButton(string Title, CustomCommandAction Action);

public sealed record CustomCommandUi(string Title, string Description, IReadOnlyList<CustomCommandControl> Controls, IReadOnlyList<CustomCommandButton> Buttons);

/// <summary>
/// カスタムコマンド 1 つ（custom-commands.json の配列の 1 要素）。
/// target は `revision` / `repository` / `file` / `ref` / `submodule`。名前の `/` はサブメニュー。
/// </summary>
public sealed record CustomCommand(string Name, string Target, CustomCommandSource Source)
{
    public IReadOnlyList<string> RefTargets { get; init; } = [];
    public string? Os { get; init; }
    public CustomCommandAction? Action { get; init; }
    public CustomCommandUi? Ui { get; init; }

    /// <summary>Windows で出すか。`macOS` だけ隠す（綴りが違う値は OS 指定なしとして扱う）。</summary>
    public bool IsForWindows => Os != "macOS";

    /// <summary>ref の種類に出すか（refTargets が無ければすべて）。</summary>
    public bool AppliesTo(CustomCommandRefKind kind) => RefTargets.Count == 0 || RefTargets.Contains(kind switch
    {
        CustomCommandRefKind.LocalBranch => "localbranch",
        CustomCommandRefKind.RemoteBranch => "remotebranch",
        _ => "tag",
    });

    internal static CustomCommand? Parse(JsonNode? node, CustomCommandSource source)
    {
        if (node is not JsonObject o || o.ContainsKey("version")) return null;
        var name = CustomCommandAction.Str(o, "name");
        var target = CustomCommandAction.Str(o, "target");
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(target)) return null;
        CustomCommandUi? ui = null;
        if (o["ui"] is JsonObject u)
        {
            var controls = (u["controls"] as JsonArray ?? []).Select(CustomCommandControl.Parse).OfType<CustomCommandControl>().ToList();
            var buttons = (u["buttons"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(b => CustomCommandAction.Parse(b["action"]) is { } a ? new CustomCommandButton(CustomCommandAction.Str(b, "title") ?? "", a) : null)
                .OfType<CustomCommandButton>().ToList();
            ui = new CustomCommandUi(CustomCommandAction.Str(u, "title") ?? "", CustomCommandAction.Str(u, "description") ?? "", controls, buttons);
        }
        return new CustomCommand(name, target, source)
        {
            RefTargets = (o["refTargets"] as JsonArray ?? []).OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList(),
            Os = CustomCommandAction.Str(o, "os"),
            Action = CustomCommandAction.Parse(o["action"]),
            Ui = ui,
        };
    }
}

/// <summary>
/// カスタムコマンドの読み込み。グローバル `%LocalAppData%\GitKnife\custom-commands.json`、Shared は作業ツリーの `.gitknife/custom-commands.json`
/// （無ければ Fork の `.fork/custom-commands.json`。チームで共有しているファイルをそのまま使う）、Local は `.git/gitknife/custom-commands.json`。
/// </summary>
public static class CustomCommandStore
{
    public const string FileName = "custom-commands.json";

    public static string GlobalPath => Path.Combine(ProductInfo.AppDataDirectory, FileName);
    public static string LocalPath(string commonGitDirectory) => Path.Combine(commonGitDirectory, ProductInfo.RepositoryConfigDirectoryName, FileName);

    public static string SharedPath(string worktree)
    {
        var own = Path.Combine(worktree, ".gitknife", FileName);
        var fork = Path.Combine(worktree, ".fork", FileName);
        return !Git.HostFile.Exists(own) && Git.HostFile.Exists(fork) ? fork : own;
    }

    /// <summary>JSON（先頭の `{ "version" : 2 }` は飛ばす）。壊れていれば空。</summary>
    public static IReadOnlyList<CustomCommand> Parse(string json, CustomCommandSource source)
    {
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return (root as JsonArray ?? []).Select(n => CustomCommand.Parse(n, source)).OfType<CustomCommand>().ToList();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    public static IReadOnlyList<CustomCommand> LoadFile(string path, CustomCommandSource source)
    {
        try { return Git.HostFile.Exists(path) ? Parse(Git.HostFile.ReadAllText(path), source) : []; }
        catch (IOException) { return []; }
    }

    /// <summary>3 つのファイルを読む（グローバル → Shared → Local の順）。</summary>
    public static IReadOnlyList<CustomCommand> Load(string? worktree, string? commonGitDirectory, string? globalPath = null)
    {
        var list = new List<CustomCommand>(LoadFile(globalPath ?? GlobalPath, CustomCommandSource.Global));
        if (worktree is not null) list.AddRange(LoadFile(SharedPath(worktree), CustomCommandSource.Shared));
        if (commonGitDirectory is not null) list.AddRange(LoadFile(LocalPath(commonGitDirectory), CustomCommandSource.Local));
        return list;
    }

    /// <summary>
    /// 保存する（書式：先頭に `{ "version" : 2 }`、コマンドは名前順、キーは名前順、`"key" : value`、インデント 2）。
    /// グローバル以外は空になったらファイルを消す（Local の最後の 1 つを Shared に移したときなど）。
    /// </summary>
    public static void Save(string path, IEnumerable<CustomCommand> commands, bool keepEmptyFile)
    {
        var list = commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0 && !keepEmptyFile)
        {
            if (Git.HostFile.Exists(path)) Git.HostFile.Delete(path);
            return;
        }
        Git.HostDirectory.CreateDirectory(Path.GetDirectoryName(path)!);
        Git.HostFile.WriteAllText(path, Serialize(list));
    }

    public static string Serialize(IEnumerable<CustomCommand> commands)
    {
        var array = new JsonArray(new JsonObject { ["version"] = 2 });
        foreach (var c in commands) array.Add(ToJson(c));
        var sb = new System.Text.StringBuilder();
        Write(sb, array, 0);
        return sb.ToString();   // 改行は LF、最後に改行なし
    }

    public static JsonObject ToJson(CustomCommand c)
    {
        var o = new JsonObject { ["name"] = c.Name, ["target"] = c.Target };
        if (c.Os is { } os) o["os"] = os;
        if (c.Target == "ref") o["refTargets"] = new JsonArray(c.RefTargets.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        if (c.Ui is { } ui)
            o["ui"] = new JsonObject
            {
                ["title"] = ui.Title,
                ["description"] = ui.Description,
                ["controls"] = new JsonArray(ui.Controls.Select(ToJson).ToArray<JsonNode?>()),
                ["buttons"] = new JsonArray(ui.Buttons.Select(b => (JsonNode)new JsonObject { ["title"] = b.Title, ["action"] = ToJson(b.Action) }).ToArray()),
            };
        else if (c.Action is { } a) o["action"] = ToJson(a);
        return o;
    }

    /// <summary>動作の種類ごとに使うキーだけ出す。</summary>
    public static JsonObject ToJson(CustomCommandAction a) => a.Type switch
    {
        "process" => new JsonObject { ["type"] = "process", ["path"] = a.Path ?? "", ["args"] = a.Args ?? "", ["showOutput"] = a.ShowOutput, ["waitForExit"] = a.WaitForExit },
        "sh" => new JsonObject { ["type"] = "sh", ["script"] = a.Script ?? "", ["showOutput"] = a.ShowOutput, ["waitForExit"] = a.WaitForExit },
        "url" => new JsonObject { ["type"] = "url", ["url"] = a.Url ?? "" },
        _ => new JsonObject { ["type"] = a.Type },
    };

    /// <summary>コントロールの種類ごとに使うキーだけ出す。</summary>
    public static JsonObject ToJson(CustomCommandControl c) => c.Type switch
    {
        "textBox" when c.TextBoxType == "filepath" => new JsonObject
        {
            ["type"] = "textBox", ["textBoxType"] = "filepath", ["title"] = c.Title,
            ["dialogType"] = c.DialogType, ["defaultDirectory"] = c.DefaultDirectory, ["filename"] = c.Filename,
        },
        "textBox" => new JsonObject { ["type"] = "textBox", ["textBoxType"] = "generic", ["title"] = c.Title, ["text"] = c.Text, ["placeholder"] = c.Placeholder },
        "dropdown" => new JsonObject { ["type"] = "dropdown", ["dropdownType"] = "references", ["title"] = c.Title, ["filter"] = c.Filter },
        _ => new JsonObject { ["type"] = c.Type, ["title"] = c.Title, ["checkedValue"] = c.CheckedValue, ["uncheckedValue"] = c.UncheckedValue, ["defaultValue"] = c.DefaultValue },
    };

    private static readonly System.Text.Json.JsonSerializerOptions StringOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void Write(System.Text.StringBuilder sb, JsonNode? node, int indent)
    {
        var pad = new string(' ', indent + 2);
        switch (node)
        {
            case JsonObject o:
                sb.Append("{\n");
                var props = o.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
                for (var i = 0; i < props.Count; i++)
                {
                    sb.Append(pad).Append(System.Text.Json.JsonSerializer.Serialize(props[i].Key, StringOptions)).Append(" : ");
                    Write(sb, props[i].Value, indent + 2);
                    sb.Append(i < props.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(' ', indent).Append('}');
                break;
            case JsonArray a when a.Count == 0:
                sb.Append("[]");
                break;
            case JsonArray a:
                sb.Append("[\n");
                for (var i = 0; i < a.Count; i++)
                {
                    sb.Append(pad);
                    Write(sb, a[i], indent + 2);
                    sb.Append(i < a.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(' ', indent).Append(']');
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                sb.Append(System.Text.Json.JsonSerializer.Serialize(s, StringOptions));
                break;
            default:
                sb.Append(node?.ToJsonString() ?? "null");
                break;
        }
    }

    /// <summary>メニューに出すコマンド（target が合い、Windows 向けのもの。グループの順を保ち、グループの中は名前順）。</summary>
    public static IReadOnlyList<CustomCommand> For(IEnumerable<CustomCommand> all, string target, CustomCommandRefKind? refKind = null)
        => all.Where(c => c.Target == target && c.IsForWindows && (refKind is not { } k || c.AppliesTo(k)))
              .OrderBy(c => c.Source).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>メニューの節（フォルダならサブメニュー）。</summary>
public sealed class CustomCommandMenuNode(string title, CustomCommand? command)
{
    public string Title { get; } = title;
    public CustomCommand? Command { get; } = command;
    public List<CustomCommandMenuNode> Children { get; } = [];

    /// <summary>名前の `/` で木にする（"Tools/Echo SHA" → Tools > Echo SHA）。同じ名前のフォルダはまとめる。</summary>
    public static IReadOnlyList<CustomCommandMenuNode> Build(IEnumerable<CustomCommand> commands)
    {
        var root = new CustomCommandMenuNode("", null);
        foreach (var c in commands)
        {
            var parts = c.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;
            var node = root;
            foreach (var folder in parts[..^1])
            {
                var next = node.Children.FirstOrDefault(n => n.Command is null && n.Title == folder);
                if (next is null) node.Children.Add(next = new CustomCommandMenuNode(folder, null));
                node = next;
            }
            node.Children.Add(new CustomCommandMenuNode(parts[^1], c));
        }
        return root.Children;
    }
}

/// <summary>コマンドを実行する場面の値（target ごとに使う変数が違う）。</summary>
public sealed record CustomCommandContext(string RepositoryPath)
{
    public string? Sha { get; init; }
    public string? ShortSha { get; init; }
    public string? File { get; init; }
    /// <summary>ブランチの名前（リモートなら origin/main）、リモート名なし、完全な ref。</summary>
    public string? Ref { get; init; }
    public string? RefShort { get; init; }
    public string? RefFull { get; init; }
    public string? Submodule { get; init; }
    /// <summary>UI のコントロールの値（1 番目から）。</summary>
    public IReadOnlyList<string> Controls { get; init; } = [];
}

/// <summary>
/// 変数の展開（新しい書式 `${repo:name}` と旧書式 `$repository`、UI の値 `$1{text}`）。値はそのまま埋め込む（引用符は付けない）。
/// </summary>
public static partial class CustomCommandVariables
{
    // 1, 2：UI の値 `$1{text}`。3：`${repo:name}`。4：旧書式 `$repository`
    [GeneratedRegex(@"\$(\d+)\{([a-z]+)\}|\$\{([a-z]+(?::[a-z]+)?)\}|\$(repository|SHA|sha|filepath|filename|fullreference|shortname|name|path)\b")]
    private static partial Regex Variable();

    public static string Expand(string? text, CustomCommandContext ctx, string target)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var repoName = Path.GetFileName(ctx.RepositoryPath.TrimEnd('/', '\\'));
        var shortSha = ctx.ShortSha ?? (ctx.Sha is { Length: > 7 } s ? s[..7] : ctx.Sha);
        static string FileName(string f) => f[(f.LastIndexOfAny(['/', '\\']) + 1)..];
        // 値の中の `$` を後の置き換えで拾わないよう、1 回の走査で置き換える
        return Variable().Replace(text, m =>
        {
            if (m.Groups[1].Success)
            {
                var i = int.Parse(m.Groups[1].Value) - 1;
                return i >= 0 && i < ctx.Controls.Count ? ctx.Controls[i] : m.Value;
            }
            if (m.Groups[3].Success)
                return m.Groups[3].Value switch
                {
                    "repo:name" => repoName,
                    "repo:path" => ctx.RepositoryPath,
                    "sha" => ctx.Sha ?? m.Value,
                    "sha:abbr" => shortSha ?? m.Value,
                    "file" => ctx.File ?? m.Value,
                    "file:name" => ctx.File is { } f ? FileName(f) : m.Value,
                    "ref" => ctx.Ref ?? m.Value,
                    "ref:short" => ctx.RefShort ?? m.Value,
                    "ref:full" => ctx.RefFull ?? m.Value,
                    "submodule" => ctx.Submodule ?? m.Value,
                    _ => m.Value,
                };
            return m.Groups[4].Value switch
            {
                "repository" => repoName,
                "SHA" => ctx.Sha ?? m.Value,
                "sha" => shortSha ?? m.Value,
                "filepath" => ctx.File ?? m.Value,
                "filename" => ctx.File is { } f2 ? FileName(f2) : m.Value,
                "fullreference" => ctx.RefFull ?? m.Value,
                "shortname" => ctx.RefShort ?? m.Value,
                // $name は ref ではブランチ名、submodule では submodule
                "name" => target == "submodule" ? ctx.Submodule ?? m.Value : ctx.Ref ?? m.Value,
                "path" => target == "submodule" ? ctx.Submodule ?? m.Value : m.Value,
                _ => m.Value,
            };
        });
    }
}
