using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitKnife.Core.Settings;

/// <summary>settings.json の読み書き。壊れていても起動できるように、読めなければ既定値を返す。書き込みは一時ファイル経由で置き換える。</summary>
public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,            // PascalCase
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath => Path.Combine(ProductInfo.AppDataDirectory, "settings.json");

    /// <summary>読んで、インストーラがダウンロード元を書き残していれば、送り先をその配布先にそろえる（無ければ設定のまま）。</summary>
    public static AppSettings Load(string? path = null)
    {
        var s = Read(path);
        if (Distribution.FromInstallSource) s.ApplyDistribution(Distribution.ServerUrl, Distribution.Repository);
        return s;
    }

    private static AppSettings Read(string? path)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // 壊れたファイルは退避して既定値で始める
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { /* 退避できなくても続ける */ }
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // BOM 付き
        File.Move(tmp, path, overwrite: true);
    }
}
