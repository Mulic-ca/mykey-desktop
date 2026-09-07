using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyKey.Desktop;

public partial class MainWindow
{
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _expiryTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Point _dragStart;
    private Button? _dragHandle;
    private Button _updateButton = null!;
    private TextBlock _updateStatus = null!;
    private bool _checkingUpdate;

    private void InitializeOrganization()
    {
        RecordDialogs.StyleComboBox(SortSelector);
        _expiryTimer.Tick += (_, _) =>
        {
            try { _repository.PurgeExpired(); if (_activeView == "trash") ApplyFilter(); }
            catch (Exception ex) { ShowToast("回收站清理失败：" + ex.Message); }
        };
        _expiryTimer.Start();
        Closed += (_, _) => _expiryTimer.Stop();

        var themeLabel = new TextBlock { Text = "配色", FontSize = 14, Margin = new(0,18,0,10), Foreground = ThemeManager.Brush("Ink") };
        themeLabel.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        AppearanceSettingsPanel.Children.Add(themeLabel);
        var swatches = new WrapPanel();
        foreach (var theme in ThemeManager.Choices)
        {
            var radio = new RadioButton { GroupName = "Theme", Content = theme.Name, Tag = theme.Id, IsChecked = theme.Id == _settings.Theme,
                Foreground = ThemeManager.Brush("Ink"), Margin = new(0,0,18,12), VerticalContentAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new(3), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Accent)),
                BorderBrush = ThemeManager.Brush("SoftBorder"), BorderThickness = new(1), Margin = new(0,0,6,0) });
            content.Children.Add(new TextBlock { Text = theme.Name, VerticalAlignment = VerticalAlignment.Center });
            radio.Content = content;
            radio.SetResourceReference(Control.ForegroundProperty, "Ink");
            System.Windows.Automation.AutomationProperties.SetName(radio, theme.Name);
            radio.Checked += (_, _) =>
            {
                _settings.Theme = theme.Id;
                ThemeManager.Apply(theme.Id);
                _settingsStore.Save(_settings);
                ApplyFilter();
            };
            swatches.Children.Add(radio);
        }
        AppearanceSettingsPanel.Children.Add(swatches);

        var updateRow = new StackPanel { Margin = new(0,18,0,0) };
        var autoCheck = new CheckBox { Content = "启动时检查更新", IsChecked = _settings.CheckUpdatesOnStartup, Foreground = ThemeManager.Brush("Ink"), Margin = new(0,0,0,10) };
        autoCheck.SetResourceReference(Control.ForegroundProperty, "Ink");
        autoCheck.Click += (_, _) => { _settings.CheckUpdatesOnStartup = autoCheck.IsChecked == true; _settingsStore.Save(_settings); };
        updateRow.Children.Add(autoCheck);
        _updateButton = new Button { Content = "检查更新", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left };
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync(true);
        updateRow.Children.Add(_updateButton);
        _updateStatus = new TextBlock { Text = "当前版本 " + UpdateService.CurrentVersion.ToString(3), Foreground = ThemeManager.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,0) };
        _updateStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        updateRow.Children.Add(_updateStatus);
        GeneralSettingsPanel.Children.Add(updateRow);
    }

    private void SetContentView()
    {
        TrashList.Visibility = Visibility.Collapsed;
        OrganizeBar.Visibility = Visibility.Visible;
        AddButton.Visibility = Visibility.Visible;
        SetNavState(TrashNavButton, false);
        _selectedTags.Clear();
        RenderTagFilters();
    }

    private void TrashNavButton_Click(object sender, RoutedEventArgs e)
    {
        _activeView = "trash";
        PageTitleText.Text = "回收站";
        AddButton.Visibility = OrganizeBar.Visibility = ApiList.Visibility = AccountList.Visibility = Visibility.Collapsed;
        TrashList.Visibility = Visibility.Visible;
        SetNavState(ApiNavButton, false);
        SetNavState(AccountNavButton, false);
        SetNavState(TrashNavButton, true);
        ApplyFilter();
    }

    private void RenderTagFilters()
    {
        if (TagFilterPanel is null) return;
        var tags = (_activeView == "api" ? _apiKeys.SelectMany(a => a.Tags) : _accountGroups.SelectMany(a => a.Tags))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _selectedTags.IntersectWith(tags);
        TagFilterPanel.Children.Clear();
        foreach (var tag in tags)
        {
            var check = new CheckBox { Content = tag, IsChecked = _selectedTags.Contains(tag), Foreground = ThemeManager.Brush("Ink"), Margin = new(0,4,14,4), VerticalContentAlignment = VerticalAlignment.Center };
            check.SetResourceReference(Control.ForegroundProperty, "Ink");
            check.Click += (_, _) => { if (check.IsChecked == true) _selectedTags.Add(tag); else _selectedTags.Remove(tag); ApplyFilter(); };
            TagFilterPanel.Children.Add(check);
        }
    }

    private bool TagsMatch(IReadOnlyList<string> tags) => _selectedTags.All(selected => tags.Contains(selected, StringComparer.OrdinalIgnoreCase));
    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        if (!_selectedTags.Add(tag)) _selectedTags.Remove(tag);
        RenderTagFilters();
        ApplyFilter();
    }

    private IEnumerable<ApiKeyRecord> SortApis(IEnumerable<ApiKeyRecord> source) => SortSelector.SelectedIndex switch
    {
        1 => source.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.Id),
        2 => source.OrderByDescending(a => a.IsPinned).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => source.OrderByDescending(a => a.IsPinned).ThenBy(a => a.CardOrder).ThenByDescending(a => a.Id)
    };
    private IEnumerable<AccountGroupView> SortGroups(IEnumerable<AccountGroupView> source) => SortSelector.SelectedIndex switch
    {
        1 => source.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.Accounts.Max(x => x.Id)),
        2 => source.OrderByDescending(a => a.IsPinned).ThenBy(a => a.WebsiteTitle, StringComparer.CurrentCultureIgnoreCase),
        _ => source.OrderByDescending(a => a.IsPinned).ThenBy(a => a.CardOrder).ThenByDescending(a => a.Accounts.Max(x => x.Id))
    };
    private void SortSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void DragHandle_Down(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragHandle = sender as Button;
    }

    private void DragHandle_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button handle || handle != _dragHandle) return;
        var distance = e.GetPosition(this) - _dragStart;
        if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragHandle = null;
        if (SortSelector.SelectedIndex != 0 || _selectedTags.Count > 0 || !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            ShowToast("请清除搜索和标签筛选，并选择自定义排序");
            return;
        }
        DragDrop.DoDragDrop(handle, new DataObject("MYKEY.Card", handle.Tag), DragDropEffects.Move);
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDrop(sender, e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        var y = e.GetPosition(CardScroll).Y;
        if (y < 45) CardScroll.ScrollToVerticalOffset(CardScroll.VerticalOffset - 18);
        else if (y > CardScroll.ActualHeight - 45) CardScroll.ScrollToVerticalOffset(CardScroll.VerticalOffset + 18);
    }

    private bool CanDrop(object sender, DragEventArgs e)
    {
        if (SortSelector.SelectedIndex != 0 || _selectedTags.Count > 0 || SearchBox.Text.Trim().Length > 0) return false;
        var source = e.Data.GetData("MYKEY.Card");
        var target = (sender as FrameworkElement)?.DataContext;
        return (source, target) switch
        {
            (ApiKeyRecord a, ApiKeyRecord b) => a.Id != b.Id && a.IsPinned == b.IsPinned,
            (AccountGroupView a, AccountGroupView b) => a.Key != b.Key && a.IsPinned == b.IsPinned,
            _ => false
        };
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!CanDrop(sender, e)) return;
        var source = e.Data.GetData("MYKEY.Card");
        var target = ((FrameworkElement)sender).DataContext;
        try
        {
            if (source is ApiKeyRecord a && target is ApiKeyRecord b)
            {
                var list = SortApis(_apiKeys).ToList();
                var destination = list.FindIndex(x => x.Id == b.Id);
                list.RemoveAll(x => x.Id == a.Id);
                list.Insert(destination, a);
                _repository.SaveCardOrder(true, list.Select(x => (IReadOnlyList<int>)new[] { x.Id }));
            }
            else if (source is AccountGroupView x && target is AccountGroupView y)
            {
                var list = SortGroups(_accountGroups).ToList();
                var destination = list.FindIndex(a => a.Key == y.Key);
                list.RemoveAll(a => a.Key == x.Key);
                list.Insert(destination, x);
                _repository.SaveCardOrder(false, list.Select(a => (IReadOnlyList<int>)a.Accounts.Select(b => b.Id).ToArray()));
            }
            ReloadAfterChange("排序已保存");
        }
        catch (Exception ex) { ShowToast("排序保存失败：" + ex.Message); }
    }

    private void RestoreTrash_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrashItem item }) return;
        try { var restored = _repository.RestoreTrash(item); ReloadAfterChange(restored ? "卡片已恢复" : "记录已到期清理"); }
        catch (Exception ex) { ShowToast("恢复失败：" + ex.Message); }
    }

    private void DeleteTrash_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrashItem item }) return;
        if (!RecordDialogs.ShowConfirmDialog(this, $"永久删除“{item.Title}”？此操作无法恢复", "永久删除", "永久删除")) return;
        try { _repository.DeleteTrash(item); ApplyFilter(); ShowToast("已永久删除"); }
        catch (Exception ex) { ShowToast("删除失败：" + ex.Message); }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        _updateButton.IsEnabled = false;
        _updateStatus.Text = "正在检查更新";
        try
        {
            var release = await UpdateService.CheckAsync();
            _updateStatus.Text = release is null ? "已是最新版本 · " + UpdateService.CurrentVersion.ToString(3) : "发现新版本 " + release.Version;
            if (release is not null)
            {
                if (!interactive) { ShowToast("发现新版本 " + release.Version + "，可在设置中更新"); return; }
                if (RecordDialogs.ShowUpdateDialog(this, release, _repository) is { } installer)
                {
                    UpdateService.StartInstaller(installer);
                    ExitApplication();
                }
            }
        }
        catch (Exception ex) { _updateStatus.Text = "检查失败：" + ex.Message; }
        finally { _checkingUpdate = false; _updateButton.IsEnabled = true; }
    }
}
