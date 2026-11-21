using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MyKey.Desktop;

// Owned by App, not MainWindow: Windows can activate the tray before WPF creates a window.
internal sealed class TrayIcon : IDisposable
{
    private readonly Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.NotifyIcon _icon;

    internal TrayIcon(Action show, Action exit)
    {
        var asset = System.Windows.Application.GetResourceStream(new Uri(
            "pack://application:,,,/MYKEY;component/Assets/Icons/mykey-key.ico"));
        using var stream = asset?.Stream;
        using var image = stream is null ? null : new Drawing.Icon(stream);
        _image = (Drawing.Icon)(image ?? Drawing.SystemIcons.Application).Clone();
        _menu.Items.Add("显示程序窗口", null, (_, _) => show());
        _menu.Items.Add("退出程序", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "MYKEY", ContextMenuStrip = _menu };
        _icon.DoubleClick += (_, _) => show();
        _icon.Visible = true;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
