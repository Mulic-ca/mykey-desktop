using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MyKey.Desktop;

public partial class MainWindow : Window
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "MYKEY";
    private readonly MyKeyRepository _repository;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly Dictionary<string, int> _accountGroupActive = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ApiKeyRecord> _apiKeys = [];
    private IReadOnlyList<AccountRecord> _accounts = [];
    private IReadOnlyList<AccountGroupView> _accountGroups = [];
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly bool _trayLaunch = Environment.GetCommandLineArgs().Any(arg =>
        string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
    private readonly Forms.NotifyIcon _trayIcon = new();
    private AppSettings _settings = new();
    private string _activeView = "api";
    private bool _updatingStartupToggle;
    private bool _updatingCloseToTrayToggle;
    private bool _forceExit;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        InitializeOrganization();
        InitializeTrayIcon();
        if (_trayLaunch)
        {
            WindowState = WindowState.Minimized;
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        _repository = new MyKeyRepository();
        _toastTimer.Tick += ToastTimer_Tick;
        _searchTimer.Tick += SearchTimer_Tick;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadData();
        ShowApiView();
        if (_trayLaunch)
            HideToTray();
        if (_settings.CheckUpdatesOnStartup && !AppPaths.IsTestMode)
            _ = CheckForUpdatesAsync(false);
        var updateResult = Path.Combine(AppPaths.DataDirectory, "update-result.json");
        if (File.Exists(updateResult))
        {
            try
            {
                using var result = System.Text.Json.JsonDocument.Parse(File.ReadAllText(updateResult));
                ShowToast(result.RootElement.GetProperty("success").GetBoolean() ? "更新已完成" : "更新未完成，请重试", TimeSpan.FromSeconds(4));
                File.Delete(updateResult);
            }
            catch (Exception ex) { ShowToast("无法读取更新结果：" + ex.Message); }
        }
    }

    private void LoadData()
    {
        try
        {
            _apiKeys = _repository.LoadApiKeys();
            _accounts = _repository.LoadAccounts();
            _accountGroups = AccountGrouping.BuildGroups(_accounts, _accountGroupActive);
            DatabasePathText.Text = _repository.DatabasePath;
            RenderTagFilters();
        }
        catch (Exception ex)
        {
            ShowToast("加载失败：" + ex.Message, TimeSpan.FromSeconds(3.2));
        }
    }

    private void ApiNavButton_Click(object sender, RoutedEventArgs e) => ShowApiView();

    private void AccountNavButton_Click(object sender, RoutedEventArgs e) => ShowAccountView();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplyFilter();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SyncSettingsState();
        SetSettingsCategory("general");
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void GeneralSettingsNavButton_Click(object sender, RoutedEventArgs e) => SetSettingsCategory("general");

    private void AppearanceSettingsNavButton_Click(object sender, RoutedEventArgs e) => SetSettingsCategory("appearance");

    private void DatabaseSettingsNavButton_Click(object sender, RoutedEventArgs e) => SetSettingsCategory("database");

    private void ExportDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出数据库",
            Filter = "MYKEY 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            DefaultExt = ".db",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"MYKEY-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _repository.ExportDatabase(dialog.FileName);
            ShowToast("数据库已导出");
        }
        catch (Exception ex)
        {
            ShowToast("导出失败：" + ex.Message, TimeSpan.FromSeconds(3.2));
        }
    }

    private void ImportDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入数据库",
            Filter = "MYKEY 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            DefaultExt = ".db",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (!RecordDialogs.ShowConfirmDialog(
                this,
                "导入后将替换当前数据库，现有数据库会自动备份。是否继续？",
                "导入数据库",
                "确认导入"))
        {
            return;
        }

        try
        {
            _repository.ImportDatabase(dialog.FileName);
            LoadData();
            ApplyFilter();
            ShowToast("数据库已导入，原数据库已自动备份");
        }
        catch (Exception ex)
        {
            ShowToast("导入失败：" + ex.Message, TimeSpan.FromSeconds(3.2));
        }
    }

    private void SilentStartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingStartupToggle)
            return;

        try
        {
            SetSilentStartupEnabled(SilentStartupToggle.IsChecked == true);
            ShowToast(SilentStartupToggle.IsChecked == true ? "已开启启动到系统托盘" : "已关闭启动到系统托盘");
        }
        catch (Exception ex)
        {
            _updatingStartupToggle = true;
            SilentStartupToggle.IsChecked = IsSilentStartupEnabled();
            _updatingStartupToggle = false;
            ShowToast("启动项设置失败：" + ex.Message, TimeSpan.FromSeconds(3.2));
        }
    }

    private void CloseToTrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingCloseToTrayToggle)
            return;

        _settings.CloseToTray = CloseToTrayToggle.IsChecked == true;
        _settingsStore.Save(_settings);
        ShowToast(_settings.CloseToTray ? "已开启关闭到系统托盘" : "已关闭关闭到系统托盘");
    }

    private void ShowApiView()
    {
        _activeView = "api";
        SetContentView();
        PageTitleText.Text = "API 管理";
        AddButton.Content = "新增 API";
        ApiList.Visibility = Visibility.Visible;
        AccountList.Visibility = Visibility.Collapsed;
        SetNavState(ApiNavButton, true);
        SetNavState(AccountNavButton, false);
        ApplyFilter();
    }

    private void ShowAccountView()
    {
        _activeView = "accounts";
        SetContentView();
        PageTitleText.Text = "账号管理";
        AddButton.Content = "新增账号组";
        ApiList.Visibility = Visibility.Collapsed;
        AccountList.Visibility = Visibility.Visible;
        SetNavState(ApiNavButton, false);
        SetNavState(AccountNavButton, true);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!IsLoaded) return;
        var keyword = SearchBox.Text.Trim();
        if (_activeView == "trash")
        {
            var items = _repository.LoadTrash().Where(item => SearchIndex.Matches(SearchIndex.Build(item.Title), keyword)).ToList();
            TrashList.ItemsSource = items;
            EmptyStateText.Text = "回收站为空";
            EmptyStateText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        EmptyStateText.Text = "暂无匹配的卡片";

        if (_activeView == "api")
        {
            var filtered = SortApis(Filter(_apiKeys, keyword, item => item.SearchText).Where(item => TagsMatch(item.Tags))).ToList();
            ApiList.ItemsSource = filtered;
            EmptyStateText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            var filtered = FilterAccountGroups(SortGroups(_accountGroups.Where(item => TagsMatch(item.Tags))), keyword);
            AccountList.ItemsSource = filtered;
            EmptyStateText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static List<T> Filter<T>(IEnumerable<T> source, string keyword, Func<T, string> textSelector)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return source.ToList();

        return source
            .Where(item => SearchIndex.Matches(textSelector(item), keyword))
            .ToList();
    }

    private static List<AccountGroupView> FilterAccountGroups(IEnumerable<AccountGroupView> source, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return source.ToList();

        var result = new List<AccountGroupView>();
        foreach (var group in source)
        {
            if (SearchIndex.Matches(group.OwnSearchText, keyword))
            {
                result.Add(group);
                continue;
            }

            var matchedAccounts = group.Accounts
                .Where(account => SearchIndex.Matches($"{group.OwnSearchText} {account.SearchText}", keyword))
                .ToArray();
            if (matchedAccounts.Length == 0)
                continue;

            var activeAccountId = group.ActiveAccount.Id;
            var activeIndex = Array.FindIndex(matchedAccounts, account => account.Id == activeAccountId);
            result.Add(new AccountGroupView
            {
                Key = group.Key,
                Website = group.Website,
                WebsiteSub = group.WebsiteSub,
                WebsiteRemark = group.WebsiteRemark,
                Accounts = matchedAccounts,
                ActiveIndex = activeIndex < 0 ? 0 : activeIndex
            });
        }

        return result;
    }

    private static void SetNavState(Button button, bool active)
    {
        if (active) button.SetResourceReference(Control.BackgroundProperty, "Primary");
        else button.Background = Brushes.Transparent;
        button.SetResourceReference(Control.ForegroundProperty, active ? "OnPrimary" : "Muted");
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void SetSettingsCategory(string category)
    {
        var isGeneral = category == "general";
        var isAppearance = category == "appearance";
        var isDatabase = category == "database";

        GeneralSettingsPanel.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsPanel.Visibility = isAppearance ? Visibility.Visible : Visibility.Collapsed;
        DatabaseSettingsPanel.Visibility = isDatabase ? Visibility.Visible : Visibility.Collapsed;

        SetSettingsNavState(GeneralSettingsNavButton, isGeneral);
        SetSettingsNavState(AppearanceSettingsNavButton, isAppearance);
        SetSettingsNavState(DatabaseSettingsNavButton, isDatabase);
    }

    private static void SetSettingsNavState(Button button, bool active)
    {
        if (active) button.SetResourceReference(Control.BackgroundProperty, "Primary");
        else button.Background = Brushes.Transparent;
        button.SetResourceReference(Control.ForegroundProperty, active ? "OnPrimary" : "Muted");
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void SyncSettingsState()
    {
        DatabasePathText.Text = _repository.DatabasePath;
        _updatingStartupToggle = true;
        SilentStartupToggle.IsChecked = IsSilentStartupEnabled();
        _updatingStartupToggle = false;
        _updatingCloseToTrayToggle = true;
        CloseToTrayToggle.IsChecked = _settings.CloseToTray;
        _updatingCloseToTrayToggle = false;
    }

    private static bool IsSilentStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
        return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void SetSilentStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
        if (enabled)
        {
            key.SetValue(StartupValueName, $"\"{GetExecutablePath()}\" --tray");
        }
        else
        {
            key.DeleteValue(StartupValueName, false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "MYKEY.exe");
    }

    private void InitializeTrayIcon()
    {
        _trayIcon.Icon = LoadTrayIcon();
        _trayIcon.Text = "MYKEY";
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindowFromTray);

        var showItem = new Forms.ToolStripMenuItem("显示程序窗口");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowWindowFromTray);

        var exitItem = new Forms.ToolStripMenuItem("退出程序");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        _trayIcon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add(showItem);
        _trayIcon.ContextMenuStrip.Items.Add(exitItem);
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "mykey-key.ico");
        if (File.Exists(iconPath))
            return new Drawing.Icon(iconPath);

        return Drawing.Icon.ExtractAssociatedIcon(GetExecutablePath()) ?? System.Drawing.SystemIcons.Application;
    }

    internal void ShowWindowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApplication()
    {
        _forceExit = true;
        _trayIcon.Visible = false;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceExit && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }

    private void AccountTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountTabView tab })
            return;

        var group = _accountGroups.FirstOrDefault(item =>
            string.Equals(item.Key, tab.GroupKey, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return;

        var fullIndex = group.Accounts
            .Select((account, index) => new { account.Id, Index = index })
            .FirstOrDefault(item => item.Id == tab.AccountId)?.Index ?? 0;
        _accountGroupActive[tab.GroupKey] = fullIndex;
        group.ActiveIndex = fullIndex;
        if (AccountList.ItemsSource is IEnumerable<AccountGroupView> visibleGroups)
        {
            var visible = visibleGroups.FirstOrDefault(item => item.Key == tab.GroupKey);
            if (visible is not null && !ReferenceEquals(visible, group))
                visible.ActiveIndex = Array.FindIndex(visible.Accounts.ToArray(), account => account.Id == tab.AccountId);
        }
    }

    private void OpenWebsiteLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string website })
            return;

        var address = website.Trim();
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            address = "https://" + address;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowToast("官网地址格式无效");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast("无法打开官网：" + ex.Message, TimeSpan.FromSeconds(3.2));
        }
    }

    private void CopyTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || string.IsNullOrEmpty(text))
            return;

        Clipboard.SetText(text);
        ShowToast("已复制到剪贴板。");
    }

    private void CopyApiBundleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record })
            return;

        Clipboard.SetText(record.CopyBundle());
        ShowToast($"已复制 {record.Name} 的 API 配置。");
    }

    private void ShowAllModelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record })
            return;

        ModelListTitleText.Text = string.IsNullOrWhiteSpace(record.Name)
            ? "全部模型"
            : $"{record.Name} · 全部模型";
        AllModelsList.ItemsSource = record.ModelTagViews;
        ModelListOverlay.Visibility = Visibility.Visible;
    }

    private void CloseModelListButton_Click(object sender, RoutedEventArgs e)
    {
        ModelListOverlay.Visibility = Visibility.Collapsed;
        AllModelsList.ItemsSource = null;
    }

    private void CopyAccountBundleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRecord record })
            return;

        Clipboard.SetText(record.CopyBundle());
        ShowToast($"已复制 {record.WebsiteTitle} / {record.RemarkLabel} 的账号信息。");
    }

    private void ShowSpecialNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountRecord record } || string.IsNullOrWhiteSpace(record.SpecialNote))
            return;

        RecordDialogs.ShowSpecialNoteDialog(this, record.SpecialNote);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeView == "api")
        {
            var data = RecordDialogs.ShowApiKeyDialog(this, null);
            if (data is null)
                return;

            _repository.SaveApiKey(data);
            ReloadAfterChange("已新增 API 记录。");
        }
        else
        {
            var data = RecordDialogs.ShowAccountGroupDialog(this, null);
            if (data is null)
                return;

            _repository.SaveAccountGroup(data);
            ReloadAfterChange("已新增账号分组。");
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadAfterChange("已刷新。");
    }

    private void EditApiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record })
            return;

        var data = RecordDialogs.ShowApiKeyDialog(this, record);
        if (data is null)
            return;

        _repository.SaveApiKey(data);
        ReloadAfterChange($"已更新 {data.Name}。");
    }

    private void PinApiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record })
            return;

        _repository.SetApiKeyPinned(record.Id, !record.IsPinned);
        ReloadAfterChange(record.IsPinned ? "已取消置顶" : "已置顶");
    }

    private void DeleteApiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApiKeyRecord record })
            return;

        if (!RecordDialogs.ShowConfirmDialog(this, $"将“{record.Name}”移入回收站？30 天内可恢复", "移入回收站", "移入回收站"))
            return;

        _repository.DeleteApiKey(record.Id);
        ReloadAfterChange("已移入回收站，30 天内可恢复");
    }

    private void EditAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountGroupView group })
            return;

        var fullGroup = ResolveFullAccountGroup(group);
        var data = RecordDialogs.ShowAccountGroupDialog(this, fullGroup);
        if (data is null)
            return;

        _repository.SaveAccountGroup(data);
        ReloadAfterChange($"已更新 {data.WebsiteRemark}。");
    }

    private void PinAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountGroupView group })
            return;

        var fullGroup = ResolveFullAccountGroup(group);
        _repository.SetAccountGroupPinned(fullGroup.Accounts.Select(account => account.Id), !fullGroup.IsPinned);
        ReloadAfterChange(fullGroup.IsPinned ? "已取消置顶" : "已置顶");
    }

    private void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountGroupView group })
            return;

        var fullGroup = ResolveFullAccountGroup(group);
        if (!RecordDialogs.ShowConfirmDialog(this, $"将“{fullGroup.WebsiteTitle}”下的全部账号移入回收站？30 天内可恢复", "移入回收站", "移入回收站"))
            return;

        _repository.DeleteAccounts(fullGroup.Accounts.Select(account => account.Id));
        _accountGroupActive.Remove(fullGroup.Key);
        ReloadAfterChange("已移入回收站，30 天内可恢复");
    }

    private AccountGroupView ResolveFullAccountGroup(AccountGroupView group)
    {
        return _accountGroups.FirstOrDefault(item =>
                   string.Equals(item.Key, group.Key, StringComparison.OrdinalIgnoreCase))
               ?? group;
    }

    private void ShowToast(string message)
    {
        ShowToast(message, TimeSpan.FromSeconds(1.8));
    }

    private void ShowToast(string message, TimeSpan duration)
    {
        ToastText.Text = message.TrimEnd('。', '.', '！', '!');
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, null);
        ToastHost.Opacity = 0;
        ToastTransform.Y = -18;
        _toastTimer.Stop();
        _toastTimer.Interval = duration;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ToastHost.BeginAnimation(OpacityProperty, fadeIn);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var slideOut = new DoubleAnimation(-10, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;
        ToastHost.BeginAnimation(OpacityProperty, fadeOut);
        ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    private void ReloadAfterChange(string message)
    {
        LoadData();
        ApplyFilter();
        ShowToast(message);
    }
}
