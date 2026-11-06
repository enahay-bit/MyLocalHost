using System.Windows;
using Microsoft.Win32;

namespace MyLocalHost.Services;

/// <summary>
/// ライト / ダークの切り替え。
/// - 標準の部品（ボタン、入力欄、タイトルバーなど）は WPF の Fluent テーマ（Application.ThemeMode）に任せる
/// - 画面に直接使う色は Themes/Light.xaml と Themes/Dark.xaml を差し替える
/// 設定値は "system"（Windows に合わせる）、"light"、"dark" のどれか。
/// </summary>
public static class ThemeService
{
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");

    private static string _setting = FollowSystem;
    private static ResourceDictionary? _colors;
    private static bool _listening;

    /// <summary>今、暗い色で表示しているか。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>表示が明るい / 暗いに切り替わったとき。</summary>
    public static event Action? Changed;

    public static string Normalize(string? setting) => setting switch
    {
        Light => Light,
        Dark => Dark,
        _ => FollowSystem,
    };

    public static void Apply(string? setting)
    {
        _setting = Normalize(setting);
        var app = Application.Current;

#pragma warning disable WPF0001 // ThemeMode は .NET 10 でも「試験的」扱い
        app.ThemeMode = _setting switch
        {
            Light => ThemeMode.Light,
            Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001

        if (!_listening)
        {
            // Windows の設定でライト / ダークが変わったら追いかける
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        UpdateColors();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_setting != FollowSystem) return;
        Application.Current?.Dispatcher.BeginInvoke(UpdateColors);
    }

    private static void UpdateColors()
    {
        var dark = _setting switch
        {
            Light => false,
            Dark => true,
            _ => WindowsUsesDarkMode(),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_colors is not null && IsDark == dark && merged.Contains(_colors)) return;

        var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (_colors is not null) merged.Remove(_colors);
        merged.Add(next);
        _colors = next;
        IsDark = dark;
        Changed?.Invoke();
    }

    /// <summary>Windows の「アプリのモード」がダークか。</summary>
    private static bool WindowsUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Shutdown()
    {
        if (_listening) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }
}
