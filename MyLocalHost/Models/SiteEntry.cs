namespace MyLocalHost.Models;

/// <summary>
/// 登録された1つのサイト。sites.json に保存される内容そのもの。
/// </summary>
public sealed class SiteEntry
{
    /// <summary>表示名</summary>
    public string Name { get; set; } = "";

    /// <summary>配信するフォルダ（絶対パス）</summary>
    public string Folder { get; set; } = "";

    /// <summary>待ち受けポート</summary>
    public int Port { get; set; }

    /// <summary>ブラウザで最初に開くページ（フォルダからの相対パス。例: index.html）</summary>
    public string StartPage { get; set; } = "index.html";
}

/// <summary>
/// sites.json のルート。将来アプリ設定を足せるようにオブジェクトで包んでおく。
/// </summary>
public sealed class SiteSettings
{
    public int Version { get; set; } = 1;

    /// <summary>最小化したらタスクトレイに入れる</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>表示テーマ: "system"（Windows に合わせる）/ "light" / "dark"</summary>
    public string Theme { get; set; } = "system";

    public List<SiteEntry> Sites { get; set; } = new();
}
