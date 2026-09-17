using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MyKey.Desktop;

internal static partial class Program
{
    private static void DetailsRepositoryTests()
    {
        var path = Path.Combine(AppPaths.DataDirectory, "details.db");
        var repo = new MyKeyRepository(path);
        repo.SaveApiKey(Api("旧平台"));
        repo.SaveAccountGroup(Group("旧账号", 1));
        using (var connection = new SqliteConnection("Data Source=" + path))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE api_keys DROP COLUMN api_secrets; DROP TABLE contact_history;";
            command.ExecuteNonQuery();
        }
        repo = new MyKeyRepository(path);
        Check(repo.LoadApiKeys().Single().DisplayKeys.Single().Value == "test-key-not-real", "1.1.0 single key migrates without loss");
        var backups = Directory.GetFiles(Path.Combine(AppPaths.DataDirectory, "backups"), "mykey-before-1.1.5-*.db");
        Check(backups.Length >= 2, "old database backed up before 1.1.5 migration");
        Check(repo.LoadContactHistory().Emails.SequenceEqual(new[] { "demo@example.com" }), "existing saved contacts seed history during migration");
        var fixture = Api("多 Key 平台");
        fixture.Keys = [new() { Value = "first-key", Remark = "日常" }, new() { Value = "second-key", Remark = "六个字的备注", IsDefault = true }];
        repo.SaveApiKey(fixture);
        var api = repo.LoadApiKeys().First(a => a.Name == fixture.Name);
        Check(api.DisplayKeys.Count == 2 && api.EffectiveKey == "second-key" && api.ApiKey == "second-key", "multiple keys persist with default and legacy mirror");
        Check(api.CopyBundle().Contains("second-key") && !api.CopyBundle().Contains("first-key"), "configuration copy uses selected default key");
        Check(SearchIndex.Matches(api.SearchText, "first-key") && SearchIndex.Matches(api.SearchText, "日常"), "key values and remarks searchable");
        var serialized = JsonSerializer.Serialize(api.DisplayKeys);
        repo.DeleteApiKey(api.Id);
        Check(!repo.UpdateDetectedModels(api, ["unexpected"]), "refresh cannot change deleted API");
        repo.RestoreTrash(repo.LoadTrash().Single());
        Check(JsonSerializer.Serialize(repo.LoadApiKeys().First(a => a.Id == api.Id).DisplayKeys) == serialized, "recycle bin preserves all keys and remarks");
        Check(repo.UpdateDetectedModels(api, ["new-model"]), "detected model refresh saved");
        var updated = repo.LoadApiKeys().First(a => a.Id == api.Id);
        Check(updated.Models.SequenceEqual(new[] { "new-model" }) && updated.DefaultModel == api.DefaultModel && updated.ManualModels.Count == api.ManualModels.Count && JsonSerializer.Serialize(updated.Keys) == serialized, "refresh preserves manual models default and all credentials");
        fixture.Id = api.Id;
        fixture.Keys = [new() { Value = "replacement", Remark = "替换", IsDefault = true }];
        repo.SaveApiKey(fixture);
        Check(!repo.UpdateDetectedModels(api, ["stale"]), "in-flight refresh cannot overwrite changed key configuration");
        fixture.Keys = [new() { Value = "invalid", Remark = "一二三四五六七" }];
        bool rejected = false;
        try { repo.SaveApiKey(fixture); } catch (ArgumentException) { rejected = true; }
        Check(rejected && repo.LoadApiKeys().First(a => a.Id == api.Id).EffectiveKey == "replacement", "seven-character remark rejected without changing saved data");
        var group = Group("历史", 1);
        group.Entries[0].Emails = [" repeat@example.com ", "REPEAT@example.com"];
        group.Entries[0].Phones = ["13900000000", "13900000000"];
        repo.SaveAccountGroup(group);
        var stored = repo.LoadAccounts().First(a => a.WebsiteRemark == "历史");
        group.Entries[0].Id = stored.Id;
        group.Entries[0].Emails = ["new@example.com"];
        group.Entries[0].Phones = [];
        repo.SaveAccountGroup(group);
        var history = new MyKeyRepository(path).LoadContactHistory();
        Check(history.Emails.Count(e => e.Equals("repeat@example.com", StringComparison.OrdinalIgnoreCase)) == 1 && history.Phones.Contains("13900000000"), "saved contact history deduplicates and survives edits and restart");
        var export = Path.Combine(AppPaths.DataDirectory, "details-export.db");
        repo.ExportDatabase(export);
        var imported = new MyKeyRepository(Path.Combine(AppPaths.DataDirectory, "details-import.db"));
        imported.ImportDatabase(export);
        Check(JsonSerializer.Serialize(imported.LoadContactHistory()) == JsonSerializer.Serialize(history), "contact history included in database export and import");
        Check(imported.LoadApiKeys().First(a => a.Id == api.Id).EffectiveKey == "replacement", "key configuration included in export and import");
        var tagged = Api("四标签");
        tagged.Tags = ["一", "二", "三", "四", "一"];
        repo.SaveApiKey(tagged);
        Check(repo.LoadApiKeys().First(a => a.Name == "四标签").Tags.Count == 4, "four tags save with duplicate normalization");
        tagged.Tags.Add("五");
        rejected = false;
        try { repo.SaveApiKey(tagged); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "API repository rejects more than four tags");
        group.Tags = ["一", "二", "三", "四", "五"];
        rejected = false;
        try { repo.SaveAccountGroup(group); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "account repository rejects more than four tags");
    }

    private static async Task ModelDetectionTests()
    {
        var requests = new List<string>();
        using (var client = new HttpClient(new ModelHandler((request, token) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            Check(request.Headers.Authorization?.Parameter == "fixture-key", "model request uses selected key");
            return Task.FromResult(new HttpResponseMessage(requests.Count == 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new StringContent(requests.Count == 1 ? "not found" : "{\"data\":[{\"id\":\"a\"},{\"id\":\"a\"},{\"id\":\"b\"}]}") });
        })))
        {
            var models = await ModelDetectionService.DetectAsync("https://example.com/api/coding", "fixture-key", client: client);
            Check(models.SequenceEqual(new[] { "a", "b" }) && requests.SequenceEqual(new[] { "/api/coding/v1/models", "/v1/models" }), "model detection preserves fallback and deduplicates models");
        }
        foreach (var (status, expected) in new[] { (401,"无效"), (403,"权限"), (404,"接口"), (405,"接口"), (429,"频繁"), (500,"服务异常"), (504,"超时") })
        {
            using var client = new HttpClient(new ModelHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("English server error fixture-secret") })));
            try { await ModelDetectionService.DetectAsync("https://example.com/v1", "fixture-key", client: client); throw new Exception("Expected detection error"); }
            catch (ModelDetectionException ex)
            {
                var message = ModelDetectionService.DescribeError(ex);
                Check(message.Contains(expected) && !message.Contains("fixture-secret") && !message.Contains("English"), "Chinese model error without raw body: " + status);
            }
        }
        foreach (var body in new[] { "<html>bad gateway</html>", "{}", "[]", "{\"data\":[{\"id\":5}]}" })
        {
            using var client = new HttpClient(new ModelHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
            Exception? failure = null;
            try { await ModelDetectionService.DetectAsync("https://example.com/v1", "fixture-key", client: client); } catch (Exception ex) { failure = ex; }
            Check(failure != null && ModelDetectionService.DescribeError(failure).Contains("平台"), "malformed success response reports Chinese error instead of erasing models");
        }
        Check(ModelDetectionService.DescribeError(new TaskCanceledException()).Contains("超时") &&
              ModelDetectionService.DescribeError(new HttpRequestException("English")).Contains("无法连接"), "network and timeout errors localized");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var cancelClient = new HttpClient(new ModelHandler((_, token) => { token.ThrowIfCancellationRequested(); throw new Exception("Unexpected call"); }));
        bool wasCancelled = false;
        try { await ModelDetectionService.DetectAsync("https://example.com/v1", "fixture-key", cancelled.Token, cancelClient); } catch (OperationCanceledException) { wasCancelled = true; }
        Check(wasCancelled, "model request respects cancellation");
    }

    private static void DetailsUiTests(MainWindow window, MyKeyRepository repo, string snapshots)
    {
        ((Button)window.FindName("ApiNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var first = repo.LoadApiKeys()[0];
        Check(FindVisual<TextBlock>(window).Any(t => t.Text == "项目开发专用"), "six-character key remark appears on API card");
        var copies = FindVisual<Button>(window).Where(b => b.ToolTip is string t && t == "复制此 Key").ToArray();
        Check(copies.Length == 4 && copies.Any(b => Equals(b.Tag, "sk-test-project-not-real")), "each key has its own copy control");
        Check(FindVisual<Button>(window).Count(b => Equals(b.Content, "刷新模型")) == 2, "each API card exposes model refresh");

        ApiKeyEditData? edited = null;
        InspectDialog(window, dialog =>
        {
            ExerciseTagEditor(dialog);
            var values = FindVisual<TextBox>(dialog).Where(t => Equals(t.Tag, "KeyValue")).ToArray();
            Check(values.Length == 2, "API editor loads multiple keys");
            var keyGrid = (Grid)values[1].Parent;
            Check(keyGrid.Children.OfType<RadioButton>().Single().IsChecked == true, "API editor preserves second key default");
            var remark = FindVisual<TextBox>(dialog).First(t => Equals(t.Tag, "KeyRemark"));
            remark.Text = "一二三四五六七";
            Check(remark.Text == "一二三四五六", "remark input limits pasted text to six characters");
            FindVisual<Button>(dialog).First(b => Equals(b.Content, "+ 添加 Key")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            values = FindVisual<TextBox>(dialog).Where(t => Equals(t.Tag, "KeyValue")).ToArray();
            values[^1].Text = "third-fixture-key";
            ((Grid)values[^1].Parent).Children.OfType<RadioButton>().Single().IsChecked = true;
            Snapshot(dialog, Path.Combine(snapshots, "multi-key-editor.png"));
            FindVisual<Button>(dialog).First(b => Equals(b.Content, "保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, () => edited = RecordDialogs.ShowApiKeyDialog(window, first));
        Check(edited?.Keys.Count == 3 && edited.Keys.Single(k => k.IsDefault).Value == "third-fixture-key", "API editor saves added key and selected default");

        InspectDialog(window, dialog =>
        {
            ExerciseTagEditor(dialog);
            Snapshot(dialog, Path.Combine(snapshots, "account-tags.png"));
            FindVisual<Button>(dialog).First(b => Equals(b.Content, "+ 添加邮箱")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var input = FindVisual<TextBox>(dialog).First(t => Equals(t.ToolTip, "email@example.com"));
            input.BringIntoView(); input.Focus(); Pump();
            var candidates = FindVisual<ListBox>(dialog).First(l => Equals(l.Tag, "ContactSuggestions") && l.Visibility == Visibility.Visible);
            Check(candidates.Items.Cast<string>().Contains("demo@example.com"), "saved email suggested below focused input");
            input.Text = "demo@"; Pump();
            Check(candidates.Items.Count == 1, "contact suggestions filter typed input");
            candidates.SelectedIndex = 0;
            candidates.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(input.Text == "demo@example.com" && candidates.Visibility == Visibility.Collapsed, "keyboard selection fills email input");
            FindVisual<Button>(dialog).First(b => Equals(b.Content, "+ 添加手机")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var phone = FindVisual<TextBox>(dialog).First(t => Equals(t.ToolTip, "手机号码"));
            phone.BringIntoView(); phone.Focus(); Pump();
            candidates = FindVisual<ListBox>(dialog).First(l => Equals(l.Tag, "ContactSuggestions") && l.Visibility == Visibility.Visible);
            Check(candidates.Items.Cast<string>().Contains("13800000000") && !candidates.Items.Cast<string>().Any(v => v.Contains('@')), "phone suggestions separate from email history");
            Snapshot(dialog, Path.Combine(snapshots, "contact-suggestions.png"));
            dialog.Close();
        }, () => RecordDialogs.ShowAccountGroupDialog(window, null, repo.LoadContactHistory()));
        ModelRefreshUiTests(window, repo, snapshots);
    }

    private static void ExerciseTagEditor(Window dialog)
    {
        var add = FindVisual<Button>(dialog).First(b => Equals(b.Content, "+ 添加标签"));
        while (FindVisual<TextBox>(dialog).Count(t => Equals(t.Tag, "TagInput")) < 4)
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var inputs = FindVisual<TextBox>(dialog).Where(t => Equals(t.Tag, "TagInput")).ToArray();
        for (int i = 0; i < inputs.Length; i++) inputs[i].Text = "标签" + (i + 1);
        Check(add.Visibility == Visibility.Collapsed && inputs.Length == 4, "tag editor limits additions to four");
        var slots = inputs.Select(t => (Grid)t.Parent).ToArray();
        Check(Grid.GetRow(slots[0]) == 0 && Grid.GetColumn(slots[1]) == 1 && Grid.GetRow(slots[2]) == 1 && Grid.GetColumn(slots[3]) == 1, "tags arranged in two columns and two rows");
        slots[1].Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(add.Visibility == Visibility.Visible && FindVisual<TextBox>(dialog).Count(t => Equals(t.Tag, "TagInput")) == 3, "tag removal keeps remaining fields and allows adding again");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        FindVisual<TextBox>(dialog).Last(t => Equals(t.Tag, "TagInput")).Text = "新标签";
    }

    private static void ModelRefreshUiTests(MainWindow window, MyKeyRepository repo, string snapshots)
    {
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var server = Task.Run(async () =>
        {
            foreach (var status in new[] { 200, 401 })
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = status;
                var bytes = System.Text.Encoding.UTF8.GetBytes(status == 200 ? "{\"data\":[{\"id\":\"refreshed-fixture\"}]}" : "Invalid API key fixture-secret");
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        var fixture = Api("本地模型刷新测试");
        fixture.AltUrls = [new() { Url = $"http://127.0.0.1:{port}/v1", IsDefault = true }];
        fixture.Models = []; fixture.ManualModels = []; fixture.DefaultModel = "";
        repo.SaveApiKey(fixture);
        void Reload() => FindVisual<Button>(window).First(b => Equals(b.Content, "刷新")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Reload(); Pump();
        Button RefreshButton() => FindVisual<Button>(window).First(b => Equals(b.Content, "刷新模型") && b.Tag is ApiKeyRecord a && a.Name == fixture.Name);
        var button = RefreshButton();
        Check(button.IsVisible, "model refresh available on a card with no models");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!button.IsEnabled && Equals(button.Content, "刷新中…"), "refresh shows busy state and disables duplicate click");
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!repo.LoadApiKeys().First(a => a.Name == fixture.Name).Models.Contains("refreshed-fixture") && DateTime.UtcNow < deadline) WaitUi(50);
        Pump();
        var refreshed = repo.LoadApiKeys().First(a => a.Name == fixture.Name);
        Check(refreshed.Models.SequenceEqual(new[] { "refreshed-fixture" }), "card refresh requests models and persists successful response");
        InspectDialog(window, dialog =>
        {
            Check(FindVisual<TextBlock>(dialog).Any(t => t.Text.Contains("API Key 无效")), "card refresh displays Chinese provider error");
            Snapshot(dialog, Path.Combine(snapshots, "model-error.png"));
            dialog.Close();
        }, () =>
        {
            RefreshButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var end = DateTime.UtcNow.AddSeconds(8);
            while (Application.Current.Windows.Count == 1 && DateTime.UtcNow < end) WaitUi(50);
        });
        Check(repo.LoadApiKeys().First(a => a.Id == refreshed.Id).Models.SequenceEqual(refreshed.Models), "failed card refresh preserves previous model list");
        Check(server.Wait(3000), "local model test server completed");
        repo.DeleteApiKey(refreshed.Id); repo.DeleteTrash(repo.LoadTrash().Single()); Reload(); Pump();
    }

    private static void InspectDialog(Window owner, Action<Window> inspect, Action show)
    {
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dialog = Application.Current.Windows.OfType<Window>().First(w => w != owner);
            try { inspect(dialog); }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        timer.Start(); show(); timer.Stop();
        if (failure != null) throw failure;
    }

    private sealed class ModelHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
