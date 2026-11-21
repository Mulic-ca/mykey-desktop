using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MyKey.Desktop;

public static partial class RecordDialogs
{
    private sealed class TagEditor
    {
        private readonly Grid grid = new() { Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly WrapPanel legacy = new() { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly List<TextBox> inputs = [];
        private readonly Button add;

        public TagEditor(Panel parent, IReadOnlyList<string> tags)
        {
            parent.Children.Add(new TextBlock { Text = "标签（最多 4 个）", Foreground = MutedBrush(), FontSize = 13, FontFamily = UiFont, Margin = new Thickness(0, 0, 0, 6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parent.Children.Add(grid);
            parent.Children.Add(legacy);
            add = CreateDashedAddButton("+ 添加标签");
            add.Width = 212;
            add.MinHeight = 32;
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 0, 0, 12);
            add.Click += (_, _) =>
            {
                if (inputs.Count >= 4) return;
                AddInput(""); Render(); inputs[^1].Focus();
            };
            parent.Children.Add(add);
            foreach (var tag in TagNames.Normalize(tags)) AddInput(tag);
            Render();
        }

        private void AddInput(string text)
        {
            var input = new TextBox { Text = text, Tag = "TagInput", ToolTip = "标签", MinHeight = 32, FontSize = 13, Padding = new Thickness(8, 4, 8, 4) };
            ApplyTextBoxStyle(input);
            inputs.Add(input);
        }

        private void Render()
        {
            foreach (var row in grid.Children.OfType<Grid>()) row.Children.Clear();
            grid.Children.Clear();
            legacy.Children.Clear();
            for (int i = 0; i < Math.Min(inputs.Count, 4); i++)
            {
                var input = inputs[i];
                var row = new Grid { Margin = new Thickness(0, 0, 8, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var remove = CreateContactRemoveButton();
                remove.ToolTip = "删除此标签";
                remove.MinHeight = 28;
                remove.Margin = new Thickness(4, 0, 0, 0);
                remove.Click += (_, _) => { inputs.Remove(input); Render(); };
                row.Children.Add(input);
                Grid.SetColumn(remove, 1); row.Children.Add(remove);
                Grid.SetColumn(row, i % 2); Grid.SetRow(row, i / 2); grid.Children.Add(row);
            }
            // Older databases can contain more than four tags. Keep them until the user removes them.
            if (inputs.Count > 4)
            {
                legacy.Children.Add(new TextBlock { Text = "超出上限的旧标签，请删除后保存：", Foreground = MutedBrush(), Margin = new Thickness(0, 0, 8, 8) });
                foreach (var input in inputs.Skip(4))
                {
                    var remove = CreateActionButton(input.Text + " ×", false);
                    remove.Margin = new Thickness(0, 0, 6, 8);
                    remove.Click += (_, _) => { inputs.Remove(input); Render(); };
                    legacy.Children.Add(remove);
                }
            }
            add.Visibility = inputs.Count < 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        public List<string> GetValues() => TagNames.Normalize(inputs.Select(input => input.Text));
        public bool Validate(Window owner)
        {
            if (GetValues().Count <= 4) return true;
            ShowNoticeDialog(owner, "每张卡片最多添加 4 个标签，请删除多余的旧标签后保存。", "标签数量超限");
            return false;
        }
    }

    private sealed class ApiSecretEditor
    {
        private readonly StackPanel rowsPanel = new();
        private readonly List<(Grid Row, TextBox Remark, TextBox Value, RadioButton Default)> rows = [];
        private readonly string groupName = "default-key-" + Guid.NewGuid().ToString("N");

        public ApiSecretEditor(Panel parent, IReadOnlyList<ApiSecretRecord> keys)
        {
            AddSectionTitle(parent, "API Key");
            parent.Children.Add(new TextBlock
            {
                Text = "备注最多 6 个字；默认 Key 用于检测模型和复制配置。",
                Foreground = MutedBrush(), FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
            });
            parent.Children.Add(rowsPanel);
            foreach (var key in keys) AddRow(key);
            if (rows.Count == 0) AddRow(new() { IsDefault = true });
            if (!rows.Any(r => r.Default.IsChecked == true)) rows[0].Default.IsChecked = true;
            var add = CreateActionButton("+ 添加 Key", false);
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.Margin = new Thickness(0, 0, 0, 8);
            add.Click += (_, _) => { AddRow(new()); rows[^1].Value.Focus(); };
            parent.Children.Add(add);
        }

        private void AddRow(ApiSecretRecord key)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var width in new[] { new GridLength(144), new GridLength(1, GridUnitType.Star), new GridLength(64), new GridLength(42) })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            var remark = new TextBox { Text = key.Remark, Tag = "KeyRemark", ToolTip = "备注（可选，最多 6 个字）", MinHeight = FieldHeight, Padding = new Thickness(10, 5, 10, 5), FontSize = 14 };
            ApplyTextBoxStyle(remark);
            remark.TextChanged += (_, _) =>
            {
                var text = new StringInfo(remark.Text);
                if (text.LengthInTextElements <= 6) return;
                var caret = remark.CaretIndex;
                remark.Text = text.SubstringByTextElements(0, 6);
                remark.CaretIndex = Math.Min(caret, remark.Text.Length);
            };
            var value = new TextBox { Text = key.Value, Tag = "KeyValue", ToolTip = "API Key", MinHeight = FieldHeight, Padding = new Thickness(10, 5, 10, 5), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
            ApplyTextBoxStyle(value);
            var isDefault = new RadioButton { Content = "默认", GroupName = groupName, IsChecked = key.IsDefault, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12.5, FontFamily = UiFont };
            var remove = CreateActionButton("×", false);
            remove.ToolTip = "删除此 Key";
            remove.Margin = new Thickness(8, 0, 0, 0);
            remove.MinWidth = 34;
            remove.Click += (_, _) =>
            {
                if (rows.Count == 1)
                {
                    ShowNoticeDialog(Window.GetWindow(rowsPanel), "至少需要保留一把 API Key。", "无法删除");
                    return;
                }
                rows.RemoveAll(r => r.Row == row);
                rowsPanel.Children.Remove(row);
                if (!rows.Any(r => r.Default.IsChecked == true)) rows[0].Default.IsChecked = true;
            };
            UIElement[] controls = [remark, value, isDefault, remove];
            for (int i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); row.Children.Add(controls[i]); }
            rows.Add((row, remark, value, isDefault));
            rowsPanel.Children.Add(row);
        }

        public List<ApiSecretRecord> GetValues() => ApiSecretRecord.Normalize(rows.Select(r => new ApiSecretRecord
        {
            Remark = r.Remark.Text, Value = r.Value.Text, IsDefault = r.Default.IsChecked == true
        }));
    }

    private static void AttachContactSuggestions(Grid row, TextBox input, IReadOnlyList<string> history)
    {
        var suggestions = new ListBox
        {
            Tag = "ContactSuggestions", MaxHeight = 120, Visibility = Visibility.Collapsed,
            Background = ThemeManager.Brush("Panel"), Foreground = InkBrush(),
            BorderBrush = ThemeManager.Brush("SoftBorder"), BorderThickness = new Thickness(1),
            FontFamily = UiFont, FontSize = 13, Margin = new Thickness(0, 4, 0, 0)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(suggestions, ScrollBarVisibility.Disabled);
        Grid.SetRow(suggestions, 1);
        row.Children.Add(suggestions);
        void UpdateSuggestions()
        {
            if (!input.IsKeyboardFocused) return;
            var query = input.Text.Trim();
            var matches = history.Where(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, query, StringComparison.OrdinalIgnoreCase)).ToArray();
            suggestions.ItemsSource = matches;
            suggestions.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (matches.Length > 0)
                suggestions.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (suggestions.IsVisible) suggestions.BringIntoView();
                });
        }
        void Choose()
        {
            if (suggestions.SelectedItem is not string value) return;
            input.Text = value;
            input.Focus();
            input.CaretIndex = input.Text.Length;
            suggestions.Visibility = Visibility.Collapsed;
        }
        input.GotKeyboardFocus += (_, _) => UpdateSuggestions();
        input.TextChanged += (_, _) => UpdateSuggestions();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { suggestions.Visibility = Visibility.Collapsed; e.Handled = true; }
            if (e.Key != Key.Down || suggestions.Visibility != Visibility.Visible) return;
            suggestions.SelectedIndex = 0;
            suggestions.UpdateLayout();
            (suggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        };
        suggestions.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(suggestions, e.OriginalSource as DependencyObject) is ListBoxItem item)
            {
                suggestions.SelectedItem = item.Content;
                Choose();
                e.Handled = true;
            }
        };
        suggestions.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == Key.Escape) { input.Focus(); suggestions.Visibility = Visibility.Collapsed; e.Handled = true; }
        };
        row.LostKeyboardFocus += (_, _) => row.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!row.IsKeyboardFocusWithin) suggestions.Visibility = Visibility.Collapsed;
        });
    }
}
