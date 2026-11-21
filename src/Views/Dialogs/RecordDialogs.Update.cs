using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MyKey.Desktop;

public static partial class RecordDialogs
{
    internal static void StyleComboBox(ComboBox combo) => ApplyComboBoxStyle(combo);

    public static string? ShowUpdateDialog(Window owner, AppRelease release, MyKeyRepository repository)
    {
        var dialog = CreateDialog(owner, "更新至 " + release.Version.ToString(3), 620, 650);
        var panel = new StackPanel();
        var notes = new TextBox { Text = release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 120, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(12), FontSize = 14, BorderThickness = new(1), BorderBrush = ThemeManager.Brush("SoftBorder") };
        ApplyTextBoxStyle(notes);
        panel.Children.Add(notes);
        var progress = new ProgressBar { Height = 5, Maximum = 100, Margin = new(0,16,0,0), Visibility = Visibility.Collapsed, Foreground = ThemeManager.Brush("Accent"), Background = ThemeManager.Brush("Subtle") };
        panel.Children.Add(progress);
        var status = new TextBlock { Foreground = ThemeManager.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,10) };
        panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = CreateActionButton("取消", false);
        var install = CreateActionButton("下载并重启更新", true);
        install.Margin = new(10,0,0,0);
        actions.Children.Add(cancel);
        actions.Children.Add(install);
        panel.Children.Add(actions);
        using var cancellation = new CancellationTokenSource();
        string? path = null;
        var closed = false;
        dialog.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        cancel.Click += (_, _) => dialog.Close();
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            status.Text = "正在下载更新";
            try
            {
                path = await UpdateService.DownloadAsync(release, new Progress<double>(value => { if (!closed) { progress.Value = value; status.Text = $"正在下载 {value:F0}%"; } }), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                status.Text = "正在备份数据库";
                repository.ExportDatabase(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "backups", $"mykey-before-update-{DateTime.Now:yyyyMMdd-HHmmss}.db"));
                dialog.DialogResult = true;
            }
            catch (OperationCanceledException) { path = null; }
            catch (Exception ex) { path = null; if (!closed) status.Text = "更新失败：" + ex.Message; }
            finally { if (!closed) install.IsEnabled = true; }
        };
        dialog.Content = WrapModalContent(dialog, panel, false);
        return dialog.ShowDialog() == true ? path : null;
    }
}
