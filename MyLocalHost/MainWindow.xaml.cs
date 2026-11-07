using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MyLocalHost.Services;
using MyLocalHost.ViewModels;

namespace MyLocalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIcon _tray;
    private bool _readyToClose;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel(new SiteStore());
        _vm.ShowEditor = editor => new EditSiteWindow(editor) { Owner = this }.ShowDialog() == true;
        DataContext = _vm;
        _vm.Load();

        // ほかのソフトがポートを使い始めた・やめたことに気づけるよう、数秒ごとに確認する
        var portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        portTimer.Tick += (_, _) => _vm.RefreshPortStatus();
        portTimer.Start();
        Activated += (_, _) => _vm.RefreshPortStatus();

        // ログは新しい行が見えるように一番下までスクロールする。
        // この通知はリスト自身より先に届くことがあり、その場でスクロールすると
        // リストの中身と件数が食い違って落ちるので、リストが追いついてから行う。
        _vm.Logs.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_vm.Logs.Count > 0)
                    LogList.ScrollIntoView(_vm.Logs[^1]);
            });
        };

        // タスクトレイ
        _tray = new TrayIcon(
            show: ShowFromTray,
            stopAll: () => _ = _vm.StopAllAsync(),
            exit: Close);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.RunningCount))
                _tray.Update(_vm.RunningCount);
        };
        _tray.Update(0);
        _tray.ApplyTheme(ThemeService.IsDark);
        ThemeService.Changed += OnThemeChanged;
    }

    private void OnThemeChanged() => _tray.ApplyTheme(ThemeService.IsDark);

    /// <summary>トレイから（または二重起動されたときに）ウィンドウを前に出す。</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _tray.Visible = false;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _vm.MinimizeToTray)
        {
            _tray.Visible = true;
            Hide();
        }
    }

    /// <summary>
    /// 閉じるときは、起動中のサーバーをすべて止めてから閉じる。
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_readyToClose || !_vm.AnyRunning) return;

        e.Cancel = true;
        IsEnabled = false;
        Title = "MyLocalHost - サーバーを停止しています…";
        try
        {
            await _vm.StopAllAsync();
        }
        finally
        {
            _readyToClose = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Changed -= OnThemeChanged;
        ThemeService.Shutdown();
        _tray.Dispose();
        base.OnClosed(e);
    }
}
