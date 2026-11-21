using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyKey.Desktop;

internal static partial class Program
{
    private static void TestPinnedCards(MainWindow window, MyKeyRepository repo, string snapshots)
    {
        var api = repo.LoadApiKeys().First();
        var accounts = repo.LoadAccounts();
        var group = AccountGrouping.BuildGroups(accounts, new Dictionary<string, int>()).First();
        repo.SetApiKeyPinned(api.Id, true);
        repo.SetAccountGroupPinned(group.Accounts.Select(x => x.Id), true);
        var refresh = FindVisual<Button>(window).First(x => Equals(x.Content, "刷新"));
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var theme in new[] { "light", "dark", "jade", "ocean", "rose" })
            {
                ThemeManager.Apply(theme);
                foreach (var (nav, name) in new[] { ("ApiNavButton", "ApiList"), ("AccountNavButton", "AccountList") })
                {
                    ClickNav(window, nav);
                    var list = (ItemsControl)window.FindName(name);
                    FindCardScroll(list).ScrollToTop(); Pump();
                    var cards = FindVisual<Border>(list).Where(b => b.AllowDrop).ToArray();
                    var pinned = cards.First(b => b.DataContext is ApiKeyRecord { IsPinned: true } or AccountGroupView { IsPinned: true });
                    Check(FindVisual<TextBlock>(pinned).Any(t => t.IsVisible && t.Text == "已置顶") &&
                          ReferenceEquals(pinned.Background, ThemeManager.Brush("PinnedCardSurface")) &&
                          !ReferenceEquals(cards.First(b => b.DataContext is ApiKeyRecord { IsPinned: false } or AccountGroupView { IsPinned: false }).Background, pinned.Background),
                          $"{name} pinned card has a visible badge and distinct surface in {theme}");
                    if (theme == "light" && name == "ApiList" || theme == "dark" && name == "AccountList")
                        Snapshot(window, Path.Combine(snapshots, $"pinned-{name}-{theme}.png"));
                }
            }
        }
        finally
        {
            repo.SetApiKeyPinned(api.Id, api.IsPinned);
            repo.SetAccountGroupPinned(group.Accounts.Select(x => x.Id), group.IsPinned);
            ThemeManager.Apply("light");
            ClickNav(window, "ApiNavButton");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        }
    }
}
