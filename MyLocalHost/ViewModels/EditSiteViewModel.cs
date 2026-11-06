using System.IO;
using MyLocalHost.Models;

namespace MyLocalHost.ViewModels;

/// <summary>
/// サイトの追加・編集ダイアログの入力内容と検証。
/// </summary>
public sealed class EditSiteViewModel : ObservableObject
{
    private readonly IReadOnlyList<SiteEntry> _others;
    private readonly Func<int, string?>? _describePortUser;
    private string _name;
    private string _folder;
    private string _portText;
    private string _startPage;
    private string _errorText = "";
    private string _warningText = "";

    /// <param name="original">編集するサイト。追加のときは null。</param>
    /// <param name="others">ほかの登録済みサイト（ポート重複の確認用）。</param>
    /// <param name="suggestedPort">追加のときに最初に入れておくポート。</param>
    /// <param name="describePortUser">ほかのプロセスがポートを使っていれば、その説明を返す。</param>
    public EditSiteViewModel(SiteEntry? original, IReadOnlyList<SiteEntry> others, int suggestedPort,
        Func<int, string?>? describePortUser = null)
    {
        _others = others;
        _describePortUser = describePortUser;
        IsNew = original is null;
        _name = original?.Name ?? "";
        _folder = original?.Folder ?? "";
        _portText = (original?.Port ?? suggestedPort).ToString();
        _startPage = original?.StartPage ?? "index.html";
        UpdateWarnings();
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "サイトの追加" : "サイトの編集";

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) ErrorText = ""; }
    }

    public string Folder
    {
        get => _folder;
        set { if (SetProperty(ref _folder, value)) { ErrorText = ""; UpdateWarnings(); } }
    }

    public string PortText
    {
        get => _portText;
        set { if (SetProperty(ref _portText, value)) { ErrorText = ""; UpdateWarnings(); } }
    }

    public string StartPage
    {
        get => _startPage;
        set { if (SetProperty(ref _startPage, value)) { ErrorText = ""; UpdateWarnings(); } }
    }

    /// <summary>保存できない理由（赤字）</summary>
    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    /// <summary>保存はできるが注意してほしいこと（橙字）</summary>
    public string WarningText
    {
        get => _warningText;
        private set => SetProperty(ref _warningText, value);
    }

    /// <summary>
    /// 最初に開くページを、フォルダの中のファイルから設定する。フォルダの外なら false。
    /// </summary>
    public bool TrySetStartPageFromFile(string filePath)
    {
        var folder = Folder.Trim();
        if (folder.Length == 0) return false;

        var rel = Path.GetRelativePath(folder, filePath);
        if (rel.StartsWith("..") || Path.IsPathRooted(rel))
        {
            ErrorText = "最初に開くページは、フォルダの中のファイルを選んでください。";
            return false;
        }

        StartPage = rel.Replace('\\', '/');
        return true;
    }

    /// <summary>入力を確認する。問題があれば ErrorText を設定して false を返す。</summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Fail("名前を入力してください。");
        if (string.IsNullOrWhiteSpace(Folder))
            return Fail("フォルダを指定してください。");
        if (!TryParsePort(out _))
            return Fail("ポートは 1〜65535 の数字で入力してください。");
        if (StartPage.Contains(".."))
            return Fail("最初に開くページに「..」は使えません。");

        ErrorText = "";
        return true;
    }

    public SiteEntry ToEntry()
    {
        TryParsePort(out var port);
        return new SiteEntry
        {
            Name = Name.Trim(),
            Folder = Folder.Trim().TrimEnd('\\', '/'),
            Port = port,
            StartPage = StartPage.Trim().Replace('\\', '/').TrimStart('/'),
        };
    }

    private bool TryParsePort(out int port)
        => int.TryParse(PortText.Trim(), out port) && port is >= 1 and <= 65535;

    private bool Fail(string message)
    {
        ErrorText = message;
        return false;
    }

    private void UpdateWarnings()
    {
        var warnings = new List<string>();
        var folder = Folder.Trim();

        if (folder.Length > 0 && !Directory.Exists(folder))
        {
            warnings.Add("このフォルダは見つかりません（別の PC 用ならこのまま保存できます）。");
        }
        else if (folder.Length > 0 && StartPage.Trim().Length > 0 &&
                 !File.Exists(Path.Combine(folder, StartPage.Trim().TrimStart('/', '\\'))))
        {
            warnings.Add("最初に開くページがフォルダの中に見つかりません。");
        }

        if (TryParsePort(out var port))
        {
            var dup = _others.FirstOrDefault(s => s.Port == port);
            if (dup is not null)
                warnings.Add($"ポート {port} は「{dup.Name}」でも使っています（同時には起動できません）。");
            else if (port < 1024)
                warnings.Add("1024 未満のポートは、ほかのソフトと重なりやすいので避けるのがおすすめです。");

            var user = _describePortUser?.Invoke(port);
            if (user is not null)
                warnings.Add($"ポート {port} は現在 {user}です（このままでは起動できません）。");
        }

        WarningText = string.Join("\n", warnings);
    }
}
