using System.Windows;
using Forms = System.Windows.Forms;

namespace MyLocalHost.Services;

/// <summary>
/// タスクトレイのアイコン。ウィンドウを隠している間だけ表示する。
/// ダブルクリックでウィンドウを戻す。右クリックで「表示 / 全停止 / 終了」。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _stopAllItem;

    public TrayIcon(Action show, Action stopAll, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => show());
        _stopAllItem = new Forms.ToolStripMenuItem("全停止", null, (_, _) => stopAll());
        menu.Items.Add(_stopAllItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了（サーバーも停止）", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "MyLocalHost",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) show();
        };
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    /// <summary>マウスを乗せたときの説明と、全停止を押せるかを更新する。</summary>
    public void Update(int runningCount)
    {
        _icon.Text = runningCount > 0 ? $"MyLocalHost（{runningCount} 件起動中）" : "MyLocalHost（停止中）";
        _stopAllItem.Enabled = runningCount > 0;
    }

    /// <summary>右クリックメニューの色を、アプリのライト / ダークに合わせる。</summary>
    public void ApplyTheme(bool dark)
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Renderer = dark
            ? new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false }
            : new Forms.ToolStripProfessionalRenderer();
        menu.BackColor = dark ? DarkMenuColors.Back : System.Drawing.SystemColors.Menu;
        foreach (Forms.ToolStripItem item in menu.Items)
            item.ForeColor = dark ? DarkMenuColors.Text : System.Drawing.SystemColors.MenuText;
    }

    /// <summary>ダークモード用のメニューの色。</summary>
    private sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        public static readonly System.Drawing.Color Back = System.Drawing.Color.FromArgb(0x2A, 0x2A, 0x2D);
        public static readonly System.Drawing.Color Text = System.Drawing.Color.FromArgb(0xF2, 0xF2, 0xF4);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(0x3C, 0x3C, 0x40);
        private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x48, 0x48, 0x4D);

        public override System.Drawing.Color ToolStripDropDownBackground => Back;
        public override System.Drawing.Color ImageMarginGradientBegin => Back;
        public override System.Drawing.Color ImageMarginGradientMiddle => Back;
        public override System.Drawing.Color ImageMarginGradientEnd => Back;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Back;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
        using var stream = info!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
