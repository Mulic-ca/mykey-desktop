using System.Windows;
using System.Windows.Controls;

namespace MyKey.Desktop;

public static partial class RecordDialogs
{
    internal static string? RequestBackupPassword(Window owner, bool exporting)
    {
        var dialog = CreateDialog(owner, exporting ? "设置备份密码" : "解锁数据库备份", 460, 410);
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var hint = new TextBlock
        {
            Text = exporting ? "换电脑时需要此密码才能恢复。MYKEY 不保存备份密码。" : "请输入导出时设置的密码。本机自动备份可留空。",
            Foreground = ThemeManager.Brush("Muted"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(hint);
        PasswordBox AddField(string label)
        {
            var box = new PasswordBox
            {
                MaxLength = 256, Padding = new Thickness(9, 6, 9, 6), MinHeight = 34,
                Background = ThemeManager.Brush("Panel"), Foreground = ThemeManager.Brush("Ink"),
                BorderBrush = ThemeManager.Brush("SoftBorder"), Margin = new Thickness(0, 4, 0, 10)
            };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            panel.Children.Add(new TextBlock { Text = label, Foreground = ThemeManager.Brush("Ink") });
            panel.Children.Add(box);
            return box;
        }
        var first = AddField(exporting ? "备份密码（至少 12 个字符）" : "备份密码");
        var confirmation = exporting ? AddField("再次输入密码") : null;
        var error = new TextBlock { Foreground = ThemeManager.Brush("Danger"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = CreateActionButton("取消", false);
        cancel.IsCancel = true;
        var submit = CreateActionButton(exporting ? "导出加密备份" : "解锁并导入", true);
        submit.IsDefault = true;
        submit.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(submit);
        panel.Children.Add(buttons);
        string? result = null;
        submit.Click += (_, _) =>
        {
            var value = first.Password;
            if (confirmation is not null)
            {
                try { DatabaseCipher.CheckPassword(value); }
                catch (ArgumentException ex) { error.Text = ex.Message; return; }
                if (confirmation.Password != value) { error.Text = "两次输入的密码不一致。"; return; }
            }
            result = value;
            dialog.DialogResult = true;
        };
        dialog.Content = WrapModalContent(dialog, panel, includeActionsBorder: false);
        dialog.Loaded += (_, _) => first.Focus();
        try { return dialog.ShowDialog() == true ? result : null; }
        finally { first.Clear(); confirmation?.Clear(); }
    }
}
