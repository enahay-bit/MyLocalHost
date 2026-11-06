using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using MyLocalHost.Models;
using MyLocalHost.Services;

namespace MyLocalHost.ViewModels;

public enum SiteStatus
{
    Stopped,
    Running,
    Warning,
}

/// <summary>
/// 一覧の1行分。SiteEntry（保存内容）と、実行中のサーバー・状態をまとめる。
/// </summary>
public sealed class SiteViewModel : ObservableObject
{
    private StaticSiteServer? _server;
    private SiteStatus _status;
    private string _statusText = "";
    private string? _lastError;
    private bool _isBusy;
    private string? _portConflict;
    private readonly Func<SiteViewModel, string?> _checkPort;

    /// <param name="checkPort">
    /// このサイトのポートを誰かが使っていれば、その説明（例:「python.exe（PID 1234）が使用中」）を返す。空いていれば null。
    /// </param>
    public SiteViewModel(SiteEntry entry, Func<SiteViewModel, string?> checkPort)
    {
        Entry = entry;
        _checkPort = checkPort;
        StartCommand = new RelayCommand(async _ => await StartAsync(openBrowser: true),
            _ => !IsBusy && !IsRunning && FolderExists);
        StopCommand = new RelayCommand(async _ => await StopAsync(),
            _ => !IsBusy && IsRunning);
        OpenBrowserCommand = new RelayCommand(_ => OpenBrowser(),
            _ => IsRunning);
        RefreshStatus();
    }

    public SiteEntry Entry { get; }

    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    public int Port => Entry.Port;
    public string StartPage => Entry.StartPage;

    public string Url => $"http://localhost:{Port}/{StartPage.TrimStart('/')}";

    public bool FolderExists => Directory.Exists(Folder);

    public bool IsRunning => _server?.IsRunning == true;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public SiteStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand OpenBrowserCommand { get; }

    /// <summary>起動・停止が終わったとき（ほかのサイトのポート表示を更新するため）。</summary>
    public event EventHandler? StateChanged;

    /// <summary>ログに出す出来事（アクセスの記録は Kestrel のスレッドから呼ばれる）。</summary>
    public event Action<SiteViewModel, string, bool>? Logged;

    private void Log(string text, bool isError = false) => Logged?.Invoke(this, text, isError);

    /// <summary>停止中に調べた、ポートを使っている相手の説明。空いていれば null。</summary>
    public string? PortConflict
    {
        get => _portConflict;
        set
        {
            if (_portConflict == value) return;
            _portConflict = value;
            RefreshStatus();
        }
    }

    public async Task StartAsync(bool openBrowser)
    {
        if (IsRunning || IsBusy) return;

        // 起動前にポートを確認する（一覧の表示は数秒ごとの更新なので、ここで改めて調べる）
        _portConflict = _checkPort(this);
        if (_portConflict is not null)
        {
            _lastError = null;
            RefreshStatus();
            Log($"起動できません: ポート {Port} は {_portConflict}", isError: true);
            return;
        }

        IsBusy = true;
        StatusText = "起動中…";
        var server = new StaticSiteServer(Folder, Port);
        server.RequestCompleted += r => Log(
            $"{r.Method} {r.Path} → {r.StatusCode}（{r.Elapsed.TotalMilliseconds:0} ms）",
            isError: r.StatusCode >= 400);
        try
        {
            await server.StartAsync();
            _server = server;
            _lastError = null;
            Log($"起動しました {Url}");
            if (openBrowser) OpenBrowser();
        }
        catch (IOException ex) when (ex is not DirectoryNotFoundException)
        {
            // Kestrel はポートが使えないとき IOException（AddressInUseException など）を投げる
            _lastError = $"ポート {Port} は使用中のため起動できません";
            Log(_lastError, isError: true);
            Debug.WriteLine(ex);
        }
        catch (Exception ex)
        {
            _lastError = $"起動できません: {ex.Message}";
            Log(_lastError, isError: true);
            Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        var server = _server;
        if (server is null) return;

        IsBusy = true;
        StatusText = "停止中…";
        try
        {
            await server.StopAsync();
            Log("停止しました");
        }
        catch (Exception ex)
        {
            Log($"停止中にエラー: {ex.Message}", isError: true);
            Debug.WriteLine(ex);
        }
        finally
        {
            _server = null;
            _lastError = null;
            IsBusy = false;
            RefreshStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>停止中かつ処理中でないとき（編集・削除してよいとき）</summary>
    public bool IsIdle => !IsRunning && !IsBusy;

    /// <summary>編集した内容を反映する（停止中のみ）。</summary>
    public void Update(SiteEntry edited)
    {
        Entry.Name = edited.Name;
        Entry.Folder = edited.Folder;
        Entry.Port = edited.Port;
        Entry.StartPage = edited.StartPage;
        _lastError = null;
        _portConflict = null; // ポートが変わったかもしれないので、次の確認で調べ直す
        OnPropertyChanged(string.Empty); // すべての表示を更新
        RefreshStatus();
    }

    public void OpenBrowser()
    {
        Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
    }

    /// <summary>起動状態・フォルダの有無・ポートの使用状況・直前のエラーから表示を更新する。</summary>
    public void RefreshStatus()
    {
        if (IsBusy) return; // 「起動中…」「停止中…」の表示を上書きしない

        OnPropertyChanged(nameof(FolderExists));
        OnPropertyChanged(nameof(IsRunning));

        if (IsRunning)
        {
            Status = SiteStatus.Running;
            StatusText = "起動中";
        }
        else if (!FolderExists)
        {
            Status = SiteStatus.Warning;
            StatusText = "フォルダが見つかりません";
        }
        else if (_portConflict is not null)
        {
            Status = SiteStatus.Warning;
            StatusText = $"ポート {Port} は {_portConflict}";
        }
        else if (_lastError is not null)
        {
            Status = SiteStatus.Warning;
            StatusText = _lastError;
        }
        else
        {
            Status = SiteStatus.Stopped;
            StatusText = "停止中";
        }

        CommandManager.InvalidateRequerySuggested();
    }
}
