using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MyKey.Desktop;

internal static partial class Program
{
    private static int passed;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (!AppPaths.IsTestMode) throw new Exception("--test-mode and --data-dir required");
            Directory.CreateDirectory(AppPaths.DataDirectory);
            RepositoryTests();
            DetailsRepositoryTests();
            ModelDetectionTests().GetAwaiter().GetResult();
            UpdateTests().GetAwaiter().GetResult();
            UiTests();
            Console.WriteLine($"TOTAL {passed} passed");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void RepositoryTests()
    {
        var path = Path.Combine(AppPaths.DataDirectory, "repository-test.db");
        // Exercise migration from the original desktop schema, not just fresh databases.
        using (var c = new SqliteConnection("Data Source=" + path))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE api_keys (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, website TEXT, base_url TEXT NOT NULL, api_key TEXT NOT NULL, models TEXT, default_model TEXT, created_at TEXT, updated_at TEXT);
                CREATE TABLE accounts (id INTEGER PRIMARY KEY AUTOINCREMENT, remark TEXT, name TEXT NOT NULL, website TEXT, password TEXT NOT NULL, emails TEXT, created_at TEXT, updated_at TEXT);
                INSERT INTO api_keys(name,base_url,api_key) VALUES ('Legacy','https://example.com','fixture');
                """;
            cmd.ExecuteNonQuery();
        }
        var repo = new MyKeyRepository(path);
        Check(repo.LoadApiKeys().Count == 1, "legacy migration preserves row");
        repo.SaveApiKey(Api("火山引擎", "工作", "开发"));
        var api = repo.LoadApiKeys().First(x => x.Name == "火山引擎");
        repo.SetApiKeyPinned(api.Id, true);
        repo.SaveCardOrder(true, new IReadOnlyList<int>[] { new[] { api.Id } });
        api = repo.LoadApiKeys().First(x => x.Id == api.Id);
        var expected = JsonSerializer.Serialize(api);
        repo.DeleteApiKey(api.Id);
        Check(repo.LoadApiKeys().All(x => x.Id != api.Id), "deleted API hidden from active rows");
        var trash = repo.LoadTrash().Single();
        Check(repo.RestoreTrash(trash), "restore API succeeds");
        Check(JsonSerializer.Serialize(repo.LoadApiKeys().First(x => x.Id == api.Id)) == expected, "API restore preserves every modeled field");
        Check(SearchIndex.Matches(api.SearchText, "hsyq") && SearchIndex.Matches(api.SearchText, "kaifa"), "pinyin initials and tag search");
        Check(api.Tags.Count == 2, "multiple tags persisted");

        var group = Group("示例网站", 3);
        group.Tags = ["工作", "常用"];
        group.IsPinned = true;
        group.CardOrder = 7;
        repo.SaveAccountGroup(group);
        var accounts = repo.LoadAccounts();
        expected = JsonSerializer.Serialize(accounts);
        repo.DeleteAccounts(accounts.Select(x => x.Id));
        trash = repo.LoadTrash().Single();
        Check(repo.LoadAccounts().Count == 0 && trash.Count == 3, "account card deleted as one batch");
        repo.RestoreTrash(trash);
        Check(JsonSerializer.Serialize(repo.LoadAccounts()) == expected, "account card restores contacts notes order tags pin");
        var edit = Group("示例网站", 0);
        edit.DeletedIds.Add(accounts[0].Id);
        edit.Tags = ["工作"];
        edit.Entries = accounts.Skip(1).Select(a => new AccountEntryEditData { Id = a.Id, Name = a.Name, Password = a.Password }).ToList();
        repo.SaveAccountGroup(edit);
        Check(repo.LoadAccounts().Count == 2 && repo.LoadTrash().Single().Count == 1, "individual account removed in editor is recoverable");
        repo.RestoreTrash(repo.LoadTrash().Single());
        Check(repo.LoadAccounts().Count == 3, "individual account restored");

        repo.DeleteApiKey(api.Id);
        var deletion = repo.LoadTrash().Single().DeletedAt;
        repo.PurgeExpired(deletion.AddDays(30).AddSeconds(-1));
        Check(repo.LoadTrash().Count == 1, "retained before 30-day boundary");
        repo.PurgeExpired(deletion.AddDays(30));
        Check(repo.LoadTrash().Count == 0 && !repo.RestoreTrash(new("api_keys", trash.Batch, "", deletion, 1)), "purged at exact 30-day boundary");

        repo.SaveApiKey(Api("Second"));
        var ids = repo.LoadApiKeys().Select(x => x.Id).ToArray();
        repo.SaveCardOrder(true, ids.Reverse().Select(id => (IReadOnlyList<int>)new[] { id }));
        Check(new MyKeyRepository(path).LoadApiKeys().Select(x => x.Id).SequenceEqual(ids.Reverse()), "custom order survives repository reopen");
        repo.DeleteApiKey(ids[0]);
        var export = Path.Combine(AppPaths.DataDirectory, "export.db");
        repo.ExportDatabase(export);
        var imported = new MyKeyRepository(Path.Combine(AppPaths.DataDirectory, "imported.db"));
        imported.ImportDatabase(export);
        Check(imported.LoadTrash().Count == 1 && imported.LoadAccounts().Count == 3, "export import includes recycle bin and account data");
        imported.DeleteTrash(imported.LoadTrash().Single());
        Check(imported.LoadTrash().Count == 0, "permanent delete");
        using (var wal = new SqliteConnection("Data Source=" + export))
        {
            wal.Open();
            using var command = wal.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO api_keys(name,base_url,api_key) VALUES ('WAL fixture','https://example.com','fixture')";
            command.ExecuteNonQuery();
            imported.ImportDatabase(export);
            Check(imported.LoadApiKeys().Any(a => a.Name == "WAL fixture"), "import snapshots committed WAL data while source remains open");
        }
    }

    private static ApiKeyEditData Api(string name, params string[] tags) => new()
    {
        Name = name, Website = "https://example.com", ApiKey = "test-key-not-real", Tags = tags.ToList(),
        DefaultModel = "model-b", Models = ["model-a", "model-b"], ManualModels = [new() { Name = "manual-c" }],
        AltUrls = [new() { Url = "https://example.com/v1", CompatType = "OpenAI兼容", IsDefault = true }, new() { Url = "https://example.org", CompatType = "Anthropic原生" }]
    };
    private static AccountGroupEditData Group(string title, int count) => new()
    {
        Website = "https://" + Uri.EscapeDataString(title) + ".example.com", WebsiteRemark = title,
        Entries = Enumerable.Range(1, count).Select(i => new AccountEntryEditData { Name = "demo-" + i, Password = "test-password", Remark = "账号" + i,
            Emails = ["demo@example.com"], Phones = ["13800000000"], SpecialNote = "可选择复制的测试备注" }).ToList()
    };

    private static async Task UpdateTests()
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic installer bytes, never executed");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var json = JsonSerializer.Serialize(new { tag_name = "v1.2.0", draft = false, prerelease = false, body = "Release notes", assets = new[] { new { name = "MYKEY-Setup-1.2.0-x64.exe", browser_download_url = "https://github.com/Mulic-ca/mykey-desktop/releases/download/v1.2.0/MYKEY-Setup-1.2.0-x64.exe", digest = "sha256:" + hash, size = bytes.Length } } });
        var release = UpdateService.ParseRelease(json, new(1, 1, 0))!;
        Check(release.Version == new Version(1,2,0), "newer stable release detected");
        Check(UpdateService.ParseRelease(json, new(1,2,0,0)) is null, "equal three/four-part version is not an update");
        Check(UpdateService.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true"), new(1,0,0)) is null, "prerelease skipped");
        bool failed = false;
        try { UpdateService.ParseRelease(json.Replace("github.com/", "evil.example/"), new(1,0,0)); } catch (InvalidDataException) { failed = true; }
        Check(failed, "reject installer outside release repository");
        using var client = new HttpClient(new FakeHandler(bytes));
        var downloads = Path.Combine(AppPaths.DataDirectory, "downloads");
        var path = await UpdateService.DownloadAsync(release, null, default, client, downloads);
        Check(File.Exists(path), "download verifies length and sha256");
        failed = false;
        try { await UpdateService.DownloadAsync(release with { Sha256 = new string('0',64) }, null, default, client, downloads); } catch (InvalidDataException) { failed = true; }
        Check(failed && !File.Exists(path + ".partial"), "checksum failure removes partial file");
        failed = false;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await UpdateService.DownloadAsync(release, null, canceled.Token, client, downloads); } catch (OperationCanceledException) { failed = true; }
        Check(failed && !File.Exists(path + ".partial"), "download cancellation cleans partial file");
    }

    private static void UiTests()
    {
        var app = new App();
        app.InitializeComponent();
        ThemeManager.Apply("light");
        var repo = new MyKeyRepository();
        foreach (var (name,tags) in new[] { ("火山引擎", new[] { "开发", "常用" }), ("示例 API", new[] { "工作" }) })
        {
            var fixture = Api(name, tags);
            fixture.Keys = [new() { Value = "sk-test-daily-not-real", Remark = "日常使用" }, new() { Value = "sk-test-project-not-real", Remark = "项目开发专用", IsDefault = true }];
            repo.SaveApiKey(fixture);
        }
        for (int i = 0; i < 30; i++) repo.SaveAccountGroup(Group("示例网站" + i, 3));
        var window = new MainWindow();
        window.Show();
        Pump();
        var apiList = (ItemsControl)window.FindName("ApiList");
        Check(apiList.Items.Count == 2, "real WPF window renders APIs");
        var search = (TextBox)window.FindName("SearchBox");
        search.Text = "hsyq"; WaitUi(220);
        Check(apiList.Items.Count == 1 && ((ApiKeyRecord)apiList.Items[0]).Name == "火山引擎", "UI pinyin search only displays matching API");
        search.Clear(); WaitUi(220);
        var tagFilter = (WrapPanel)window.FindName("TagFilterPanel");
        var work = tagFilter.Children.OfType<CheckBox>().First(c => (string)c.Content == "工作");
        work.IsChecked = true; work.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Check(apiList.Items.Count == 1 && ((ApiKeyRecord)apiList.Items[0]).Tags.Contains("工作"), "UI tag filter");
        work.IsChecked = false; work.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        ((Button)window.FindName("AccountNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var list = (ItemsControl)window.FindName("AccountList");
        var source = list.ItemsSource;
        var groups = source.Cast<AccountGroupView>().ToArray();
        var firstContainer = list.ItemContainerGenerator.ContainerFromIndex(0);
        var tab = FindVisual<Button>(firstContainer).First(b => b.Tag is AccountTabView t && t.Index == 1);
        var timer = Stopwatch.StartNew();
        tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        timer.Stop();
        Check(ReferenceEquals(source, list.ItemsSource) && ReferenceEquals(firstContainer, list.ItemContainerGenerator.ContainerFromIndex(0)), "account switching preserves list and card containers");
        Check(groups[0].ActiveIndex == 1, "account switching selects intended account");
        Console.WriteLine($"ACCOUNT_SWITCH_MS={timer.Elapsed.TotalMilliseconds:F1}");
        search.Text = "demo-2"; WaitUi(220);
        Check(list.Items.Cast<AccountGroupView>().All(g => g.Accounts.Count == 1 && g.ActiveAccount.Name == "demo-2"), "account search hides nonmatching account tabs");
        search.Clear(); WaitUi(220);
        var snapshots = Path.Combine(AppPaths.DataDirectory,"screenshots"); Directory.CreateDirectory(snapshots);
        DetailsUiTests(window, repo, snapshots);
        ((Button)window.FindName("ApiNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((ScrollViewer)window.FindName("CardScroll")).ScrollToTop();
        foreach (var theme in ThemeManager.Choices)
        {
            ThemeManager.Apply(theme.Id); Pump(); Snapshot(window,Path.Combine(snapshots,theme.Id+".png"));
            Check(((SolidColorBrush)ThemeManager.Brush("Panel")).Color != ((SolidColorBrush)ThemeManager.Brush("Ink")).Color, "theme colors distinct: " + theme.Id);
        }
        ThemeManager.Apply("dark");
        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Snapshot(window,Path.Combine(snapshots,"settings-dark.png"));
        ((Button)window.FindName("AppearanceSettingsNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Snapshot(window,Path.Combine(snapshots,"appearance-dark.png"));
        var darkRadio = FindVisual<RadioButton>(window).First(r => r.Tag is string value && value == "dark");
        darkRadio.IsChecked = true; Pump();
        Check(new AppSettingsStore().Load().Theme == "dark", "theme choice persisted through settings UI");
        ((Button)window.FindName("CloseSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        dialogTimer.Tick += (_, _) => { dialogTimer.Stop(); var dialog = app.Windows.OfType<Window>().First(w => w != window); Snapshot(dialog,Path.Combine(snapshots,"editor-dark.png")); dialog.Close(); };
        dialogTimer.Start();
        RecordDialogs.ShowApiKeyDialog(window,repo.LoadApiKeys()[0]);
        var accountDialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        accountDialogTimer.Tick += (_, _) => { accountDialogTimer.Stop(); var dialog = app.Windows.OfType<Window>().First(w => w != window); Snapshot(dialog,Path.Combine(snapshots,"account-editor-dark.png")); dialog.Close(); };
        accountDialogTimer.Start();
        RecordDialogs.ShowAccountGroupDialog(window,AccountGrouping.BuildGroups(repo.LoadAccounts(),new Dictionary<string,int>())[0]);
        repo.DeleteApiKey(repo.LoadApiKeys()[0].Id);
        ((Button)window.FindName("TrashNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var trashList = (ItemsControl)window.FindName("TrashList");
        Check(trashList.Items.Count == 1, "recycle bin navigation displays deleted card");
        Snapshot(window,Path.Combine(snapshots,"trash-dark.png"));
        FindVisual<Button>(trashList).First(b => b.Content is string text && text == "恢复").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(trashList.Items.Count == 0 && repo.LoadApiKeys().Count == 2, "restore button returns card to active collection");
        ((Button)window.FindName("ApiNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Width = 1020; window.Height = 680; Pump(); Snapshot(window,Path.Combine(snapshots,"compact-dark.png"));
        var label = FindVisual<TextBlock>(window).First(t => t.Text == "查看全部模型");
        Check(label.ActualWidth > 20 && window.ActualWidth == 1020, "compact window renders model action");
        window.Close();
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame); }
    private static void WaitUi(int milliseconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (_,_) => { timer.Stop(); frame.Continue=false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static IEnumerable<T> FindVisual<T>(DependencyObject? node) where T : DependencyObject
    {
        if (node is null) yield break;
        if (node is T item) yield return item;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(node); i++) foreach(var child in FindVisual<T>(VisualTreeHelper.GetChild(node,i))) yield return child;
    }
    private static void Snapshot(Window window,string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output=File.Create(path); encoder.Save(output);
    }
    private sealed class FakeHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
