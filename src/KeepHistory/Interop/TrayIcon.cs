using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeepHistory.Interop;

/// <summary>通知領域のアイコン（WinForms の NotifyIcon）。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayIcon(Drawing.Icon icon)
    {
        _menu = new Forms.ContextMenuStrip();
        var show = new Forms.ToolStripMenuItem("履歴を表示(&S)", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        show.Font = new Drawing.Font(show.Font, Drawing.FontStyle.Bold);
        _menu.Items.Add(show);
        _menu.Items.Add(new Forms.ToolStripMenuItem("設定(&O)...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("終了(&X)", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "KeepHistory",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void SetToolTip(string text)
    {
        // NotifyIcon.Text は 63 文字まで
        _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    public void ShowBalloon(string title, string text, bool warning = false)
        => _icon.ShowBalloonTip(5000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
