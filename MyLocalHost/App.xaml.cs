using System.Windows;

namespace MyLocalHost;

/// <summary>
/// 二重起動を防ぐ。2つ目を起動したときは、1つ目のウィンドウを前に出して終わる。
/// （同じポートでサーバーを二重に起動したり、sites.json を同時に書き換えたりしないため）
/// </summary>
public partial class App : Application
{
    private const string MutexName = "MyLocalHost.SingleInstance";
    private const string ShowEventName = "MyLocalHost.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // 1つ目に「ウィンドウを出して」と合図して終わる
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 1つ目がまだ準備中。何もせず終わる
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // まず Windows に合わせて色を用意する（sites.json を読んだら、その設定で上書きされる）
        Services.ThemeService.Apply(Services.ThemeService.FollowSystem);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // 2つ目からの合図を待つ
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (Dispatcher.HasShutdownStarted) break;
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            }
        })
        { IsBackground = true, Name = "SingleInstanceListener" };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
