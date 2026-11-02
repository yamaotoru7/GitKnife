using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

/// <summary>custom-commands.json の形と出し方。</summary>
public class CustomCommandTests
{
    private const string Json = """
        [
          { "version" : 2 },
          { "action" : { "script" : "echo SHA ${sha} ${sha:abbr} ${repo:name}", "showOutput" : true, "type" : "sh", "waitForExit" : true }, "name" : "Tools/Echo SHA", "target" : "revision" },
          { "action" : { "type" : "url", "url" : "https://example.com/${sha:abbr}" }, "name" : "Plain Commit Cmd", "target" : "revision" },
          { "action" : { "script" : "echo REF ${ref}", "type" : "sh" }, "name" : "Echo Ref", "refTargets" : [ "localbranch", "remotebranch", "tag" ], "target" : "ref" },
          { "action" : { "script" : "echo LOCALONLY ${ref}", "type" : "sh" }, "name" : "Local Only", "refTargets" : [ "localbranch" ], "target" : "ref" },
          { "action" : { "script" : "echo MAC", "type" : "sh" }, "name" : "Mac Only", "os" : "macOS", "target" : "revision" },
          { "action" : { "script" : "echo typo", "type" : "sh" }, "name" : "Typo OS", "os" : "macos", "target" : "revision" },
          { "name" : "Ask Things", "target" : "revision", "ui" : {
              "buttons" : [ { "action" : { "script" : "echo UI [$1{text}] [$2{ref}] [$3{value}]", "type" : "sh" }, "title" : "Run" }, { "action" : { "type" : "cancel" }, "title" : "Cancel" } ],
              "controls" : [
                { "placeholder" : "type here", "text" : "hello", "textBoxType" : "generic", "title" : "Message", "type" : "textBox" },
                { "dropdownType" : "references", "filter" : "refs/heads", "title" : "Branch", "type" : "dropdown" },
                { "checkedValue" : "--force", "defaultValue" : true, "title" : "Force", "type" : "checkBox", "uncheckedValue" : "" }
              ],
              "description" : "Desc", "title" : "Ask ${repo:name}" } }
        ]
        """;

    [Fact]
    public void ParsesFileAndOrdersByName()
    {
        var all = CustomCommandStore.Parse(Json, CustomCommandSource.Local);
        Assert.Equal(7, all.Count);
        // macOS だけ隠す。綴りの違う "macos" は OS 指定なしとして表示する
        var revision = CustomCommandStore.For(all, "revision");
        Assert.Equal(["Ask Things", "Plain Commit Cmd", "Tools/Echo SHA", "Typo OS"], revision.Select(c => c.Name));
        Assert.Equal(["Echo Ref"], CustomCommandStore.For(all, "ref", CustomCommandRefKind.Tag).Select(c => c.Name));
        Assert.Equal(["Echo Ref", "Local Only"], CustomCommandStore.For(all, "ref", CustomCommandRefKind.LocalBranch).Select(c => c.Name));

        var ui = all.Single(c => c.Name == "Ask Things").Ui!;
        Assert.Equal(["text", "ref", "value"], ui.Controls.Select(c => c.ValueKind));
        Assert.True(ui.Controls[2].DefaultValue);
        Assert.Equal("cancel", ui.Buttons[1].Action.Type);
        Assert.True(ui.Controls[1].MatchesFilter("refs/heads/main"));
        Assert.False(ui.Controls[1].MatchesFilter("refs/remotes/origin/main"));
        Assert.False(ui.Controls[1].MatchesFilter("refs/headsx/main"));
    }

    /// <summary>読んだファイル（Data/sample-custom-commands.json）と同じ文字列で書き戻す。</summary>
    [Fact]
    public void SerializesExactly()
    {
        var sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sample-custom-commands.json"));
        var parsed = CustomCommandStore.Parse(sample, CustomCommandSource.Local);
        Assert.Equal(8, parsed.Count);
        Assert.Equal(sample, CustomCommandStore.Serialize(parsed.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void GroupsGlobalSharedLocalAndBuildsSubmenus()
    {
        var list = new List<CustomCommand>
        {
            new("Zz Local", "revision", CustomCommandSource.Local),
            new("Tools/Echo SHA", "revision", CustomCommandSource.Local),
            new("Zed Shared", "revision", CustomCommandSource.Shared),
            new("Global Rev", "revision", CustomCommandSource.Global),
            new("Tools/Another", "revision", CustomCommandSource.Global),
        };
        var ordered = CustomCommandStore.For(list, "revision");
        Assert.Equal(["Global Rev", "Tools/Another", "Zed Shared", "Tools/Echo SHA", "Zz Local"], ordered.Select(c => c.Name));
        var menu = CustomCommandMenuNode.Build(ordered);
        Assert.Equal(["Global Rev", "Tools", "Zed Shared", "Zz Local"], menu.Select(n => n.Title));
        Assert.Equal(["Another", "Echo SHA"], menu[1].Children.Select(n => n.Title));
    }

    [Fact]
    public void ExpandsNewOldAndControlVariables()
    {
        var ctx = new CustomCommandContext(@"D:\work\basic")
        {
            Sha = "3b121caebfb81cc2d882971154e24f1cdc936c28", File = "src/app.cs",
            Ref = "origin/main", RefShort = "main", RefFull = "refs/remotes/origin/main",
            Controls = ["hello $sha", "bugfix/greeting", "--force"],
        };
        Assert.Equal("SHA 3b121caebfb81cc2d882971154e24f1cdc936c28 3b121ca basic", CustomCommandVariables.Expand("SHA ${sha} ${sha:abbr} ${repo:name}", ctx, "revision"));
        Assert.Equal(@"D:\work\basic src/app.cs app.cs", CustomCommandVariables.Expand("${repo:path} ${file} ${file:name}", ctx, "file"));
        Assert.Equal("origin/main main refs/remotes/origin/main", CustomCommandVariables.Expand("${ref} ${ref:short} ${ref:full}", ctx, "ref"));
        Assert.Equal("basic 3b121caebfb81cc2d882971154e24f1cdc936c28 3b121ca origin/main main", CustomCommandVariables.Expand("$repository $SHA $sha $name $shortname", ctx, "ref"));
        // 値に入っている `$sha` はもう一度展開しない
        Assert.Equal("UI [hello $sha] [bugfix/greeting] [--force] ${unknown}", CustomCommandVariables.Expand("UI [$1{text}] [$2{ref}] [$3{value}] ${unknown}", ctx, "revision"));
        Assert.Equal("sm/lib sm/lib", CustomCommandVariables.Expand("${submodule} $path", ctx with { Submodule = "sm/lib" }, "submodule"));
    }
}
