using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MyLocalHost.Models;

namespace MyLocalHost.Services;

/// <summary>
/// sites.json の読み書き。ファイルは exe と同じフォルダに置く（ポータブル方式）。
/// </summary>
public sealed class SiteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 日本語や \ を \uXXXX にせず、そのまま読める形で保存する
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public SiteStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "sites.json");
    }

    public string FilePath { get; }

    /// <summary>
    /// 読み込む。ファイルがなければ空の設定で作成する。
    /// JSON が壊れている場合は例外を投げる（上書きして消さないため）。
    /// </summary>
    public SiteSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(FilePath);
        var settings = JsonSerializer.Deserialize<SiteSettings>(json, JsonOptions)
                       ?? new SiteSettings();
        settings.Sites ??= new();
        return settings;
    }

    /// <summary>
    /// 保存する。一時ファイルに書いてから置き換えるので、途中で落ちても元のファイルは壊れない。
    /// </summary>
    public void Save(SiteSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>初回は空の一覧から始める（画面に「＋ 追加」の案内が出る）。</summary>
    private static SiteSettings CreateDefaults() => new();
}
