using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MyLocalHost.Models;
using MyLocalHost.Services;

namespace MyLocalHost.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int FirstPort = 8000;
    private const int MaxLogEntries = 500;

    private readonly SiteStore _store;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private SiteSettings _settings = new();

    public MainViewModel(SiteStore store)
    {
        _store = store;
        ClearLogCommand = new RelayCommand(_ => Logs.Clear(), _ => Logs.Count > 0);
        // 起動中のサーバーがあるときは読み直さない（行が入れ替わって止められなくなるため）
        ReloadCommand = new RelayCommand(_ => Load(), _ => !AnyRunning);
        StopAllCommand = new RelayCommand(async _ => await StopAllAsync(), _ => AnyRunning);
        AddCommand = new RelayCommand(_ => AddSite());
        EditCommand = new RelayCommand(p => EditSite((SiteViewModel)p!), p => p is SiteViewModel { IsIdle: true });
        DeleteCommand = new RelayCommand(p => DeleteSite((SiteViewModel)p!), p => p is SiteViewModel { IsIdle: true });
        OpenFolderCommand = new RelayCommand(
            p => OpenInExplorer((p as SiteViewModel)?.Folder),
            p => p is SiteViewModel s && s.FolderExists);
        OpenSettingsFolderCommand = new RelayCommand(_ => OpenInExplorer(Path.GetDirectoryName(SettingsPath)));
        Sites.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSites));
    }

    /// <summary>登録が 0 件のとき（画面に案内を出す）。</summary>
    public bool HasNoSites => Sites.Count == 0;

    public ObservableCollection<SiteViewModel> Sites { get; } = new();

    public ObservableCollection<LogEntry> Logs { get; } = new();

    public string SettingsPath => _store.FilePath;

    /// <summary>最小化したらタスクトレイに入れる（sites.json に保存）。</summary>
    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set
        {
            if (_settings.MinimizeToTray == value) return;
            _settings.MinimizeToTray = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>テーマの選択肢（値と表示名）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ThemeOptions { get; } =
    [
        new(ThemeService.FollowSystem, "Windows に合わせる"),
        new(ThemeService.Light, "ライト"),
        new(ThemeService.Dark, "ダーク"),
    ];

    /// <summary>表示テーマ（sites.json に保存）。</summary>
    public string Theme
    {
        get => ThemeService.Normalize(_settings.Theme);
        set
        {
            value = ThemeService.Normalize(value);
            if (Theme == value) return;
            _settings.Theme = value;
            ThemeService.Apply(value);
            OnPropertyChanged();
            Save();
        }
    }

    public int RunningCount => Sites.Count(s => s.IsRunning);

    public bool AnyRunning => Sites.Any(s => s.IsRunning || s.IsBusy);

    /// <summary>
    /// 編集ダイアログを表示する処理（画面側で設定する）。保存されたら true を返す。
    /// </summary>
    public Func<EditSiteViewModel, bool>? ShowEditor { get; set; }

    public RelayCommand ClearLogCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand StopAllCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenSettingsFolderCommand { get; }

    public void Load()
    {
        try
        {
            _settings = _store.Load();
            Sites.Clear();
            foreach (var entry in _settings.Sites)
                Sites.Add(CreateSite(entry));
            RefreshPortStatus();
            OnPropertyChanged(nameof(MinimizeToTray));
            ThemeService.Apply(_settings.Theme);
            OnPropertyChanged(nameof(Theme));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"sites.json を読み込めませんでした。\n\n{SettingsPath}\n\n{ex.Message}",
                "MyLocalHost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>起動中のサーバーをすべて止める。</summary>
    public Task StopAllAsync()
        => Task.WhenAll(Sites.Where(s => s.IsRunning).Select(s => s.StopAsync()));

    /// <summary>
    /// 停止中のサイトについて、ポートが使われていないかを調べて表示を更新する（画面側で数秒ごとに呼ぶ）。
    /// </summary>
    public void RefreshPortStatus()
    {
        var listeners = PortChecker.GetLocalhostListeners();
        foreach (var site in Sites.Where(s => s.IsIdle))
            site.PortConflict = DescribePortUser(site.Port, site, listeners);
    }

    private SiteViewModel CreateSite(SiteEntry entry)
    {
        var site = new SiteViewModel(entry, s => DescribePortUser(s.Port, s, PortChecker.GetLocalhostListeners()));
        site.StateChanged += (_, _) =>
        {
            RefreshPortStatus();
            OnPropertyChanged(nameof(RunningCount));
        };
        site.Logged += (s, text, isError) => AddLog(s.Name, text, isError);
        return site;
    }

    /// <summary>ログに1行足す。どのスレッドから呼んでもよい。</summary>
    public void AddLog(string site, string text, bool isError = false)
    {
        var entry = new LogEntry(DateTime.Now, site, text, isError);
        _dispatcher.BeginInvoke(() =>
        {
            Logs.Add(entry);
            while (Logs.Count > MaxLogEntries) Logs.RemoveAt(0);
        });
    }

    /// <summary>
    /// ポートを使っている相手の説明を返す。空いていれば null。
    /// このアプリで起動中のほかのサイト → ほかのプロセス の順に調べる。
    /// </summary>
    private string? DescribePortUser(int port, SiteViewModel? self, IReadOnlyList<PortListener> listeners)
    {
        var ownSite = Sites.FirstOrDefault(s => s != self && s.Port == port && (s.IsRunning || s.IsBusy));
        if (ownSite is not null)
            return $"「{ownSite.Name}」が起動中のため使えません";

        return DescribeOtherProcess(port, listeners);
    }

    /// <summary>このアプリ以外のプロセスがポートを使っていれば「python.exe（PID 1234）が使用中」を返す。</summary>
    private static string? DescribeOtherProcess(int port)
        => DescribeOtherProcess(port, PortChecker.GetLocalhostListeners());

    private static string? DescribeOtherProcess(int port, IReadOnlyList<PortListener> listeners)
    {
        var other = listeners.FirstOrDefault(l => l.Port == port && l.ProcessId != Environment.ProcessId);
        return other is null ? null : $"{other.ProcessLabel}が使用中";
    }

    private void AddSite()
    {
        var others = Sites.Select(s => s.Entry).ToList();
        var editor = new EditSiteViewModel(null, others, SuggestPort(), DescribeOtherProcess);
        if (ShowEditor?.Invoke(editor) != true) return;

        Sites.Add(CreateSite(editor.ToEntry()));
        RefreshPortStatus();
        Save();
    }

    private void EditSite(SiteViewModel site)
    {
        var others = Sites.Where(s => s != site).Select(s => s.Entry).ToList();
        var editor = new EditSiteViewModel(site.Entry, others, site.Port, DescribeOtherProcess);
        if (ShowEditor?.Invoke(editor) != true) return;

        site.Update(editor.ToEntry());
        RefreshPortStatus();
        Save();
    }

    private void DeleteSite(SiteViewModel site)
    {
        var answer = MessageBox.Show(
            $"「{site.Name}」を一覧から削除しますか？\n（フォルダやファイルは削除されません）",
            "サイトの削除", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        Sites.Remove(site);
        Save();
    }

    /// <summary>登録済みのポートと重ならない番号を提案する（8000 から順に）。</summary>
    private int SuggestPort()
    {
        var used = Sites.Select(s => s.Port).ToHashSet();
        var port = FirstPort;
        while (used.Contains(port)) port++;
        return port;
    }

    private void Save()
    {
        _settings.Sites = Sites.Select(s => s.Entry).ToList();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"sites.json に保存できませんでした。\n\n{SettingsPath}\n\n{ex.Message}",
                "MyLocalHost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenInExplorer(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
