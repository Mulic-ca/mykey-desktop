using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.IO;
using System.Reflection;
using MyKey.Desktop;

internal static partial class Program
{
    private static void CardViewportTests(MainWindow window, MyKeyRepository repo)
    {
        // Enough rows to distinguish virtualization from a full measure; varied lengths exercise pixel scrolling.
        for (int i = 0; i < 96; i++)
        {
            var api = Api($"viewport-api-{i:000}", "viewport");
            if (i % 2 == 0)
                api.Keys = [new() { Value = "fixture-one", IsDefault = true }, new() { Value = "fixture-two" }];
            repo.SaveApiKey(api);
            repo.SaveAccountGroup(Group($"viewport-group-{i:000}", 1 + i % 3));
        }

        ClickNav(window, "ApiNavButton");
        var refresh = FindVisual<Button>(window).First(b => b.Content is string text && text == "刷新");
        refresh.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
        var apiList = (ItemsControl)window.FindName("ApiList");
        AssertVirtualList(apiList, 96, "API");
        AssertVirtualDrag(apiList, repo);
        FindCardScroll(apiList).ScrollToEnd();
        WaitUi(200);
        var search = (TextBox)window.FindName("SearchBox");
        search.Text = "viewport-api-019";
        WaitUi(240);
        Check(apiList.Items.Count == 1 && apiList.ItemContainerGenerator.ContainerFromIndex(0) is not null, "search from deep API scroll shows the lone result");
        search.Clear();
        WaitUi(240);
        ClickNav(window, "AccountNavButton");
        var accounts = (ItemsControl)window.FindName("AccountList");
        AssertVirtualList(accounts, 96, "account");
        var allGroupKeys = AccountCards(accounts).Select(group => group.Key).ToArray();
        Check(accounts.Items.Cast<AccountGroupView[]>().All(row => row.Length == 1), "compact window shows one account card per row");
        FindCardScroll(accounts).ScrollToTop(); Pump();
        Snapshot(window, Path.Combine(AppPaths.DataDirectory, "screenshots", "accounts-one-column.png"));
        window.Width = 1320;
        Pump();
        Check(accounts.Items.Cast<AccountGroupView[]>().First().Length == 2, "wide window shows two account cards in a row");
        Check(AccountCards(accounts).Select(group => group.Key).SequenceEqual(allGroupKeys), "account reflow preserves card order and count");
        AssertVirtualList(accounts, 48, "wide account");
        FindCardScroll(accounts).ScrollToTop(); Pump();
        Snapshot(window, Path.Combine(AppPaths.DataDirectory, "screenshots", "accounts-two-columns.png"));
        AssertAccountColumnThreshold(window, accounts, search, refresh, repo);
    }

    private static void ClickNav(Window window, string name)
    {
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
    }

    private static ScrollViewer FindCardScroll(ItemsControl cards) => FindVisual<ScrollViewer>(cards).First();
    private static IEnumerable<AccountGroupView> AccountCards(ItemsControl cards) =>
        cards.Items.Cast<AccountGroupView[]>().SelectMany(row => row);

    private static void AssertAccountColumnThreshold(MainWindow window, ItemsControl cards, TextBox search,
        Button refresh, MyKeyRepository repo)
    {
        repo.SaveAccountGroup(Group("threshold-fixture-alpha", 1));
        repo.SaveAccountGroup(Group("threshold-fixture-beta", 1));
        refresh.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        search.Text = "threshold-fixture"; WaitUi(250);
        Check(AccountCards(cards).Count() == 2, "account column breakpoint fixture has exactly two cards");

        int RowsAtWidth(double width)
        {
            window.Width += width - cards.ActualWidth;
            Pump();
            Check(Math.Abs(cards.ActualWidth - width) < 2, "account width reached intended breakpoint");
            return cards.Items.Count;
        }

        RowsAtWidth(900);
        var fromNarrow = RowsAtWidth(946); // Two cards need 942 DIPs; the scrollbar needs another 10.
        Check(RowsAtWidth(970) == 1, "two account cards fit in a single row beyond the breakpoint");
        var fromWide = RowsAtWidth(946);
        Check(fromNarrow == fromWide, "account column count at the breakpoint is independent of resize direction");
    }

    private static void AssertVirtualDrag(ItemsControl list, MyKeyRepository repo)
    {
        var scroll = FindCardScroll(list);
        scroll.ScrollToTop(); Pump();
        var one = (ApiKeyRecord)list.Items[0];
        var two = (ApiKeyRecord)list.Items[1];
        var target = FindVisual<Border>(list.ItemContainerGenerator.ContainerFromIndex(1))
            .First(border => border.AllowDrop && ReferenceEquals(border.DataContext, two));

        // WPF constructs these internally for an OLE gesture. Route synthetic events through the real card.
        var constructor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
        var window = (MainWindow)Window.GetWindow(list);
        var sourceField = typeof(MainWindow).GetField("_draggedCard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tokenField = typeof(MainWindow).GetField("_dragToken", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var token = Guid.NewGuid().ToString("N");
        sourceField.SetValue(window, one);
        tokenField.SetValue(window, token);
        var data = new DataObject("MYKEY.Card", token);
        DragEventArgs Drag(RoutedEvent route, Point point) => (DragEventArgs)constructor.Invoke(
            [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, point]) is { } args
            ? WithRoute(args, route) : throw new InvalidOperationException("WPF drag construction failed");
        static DragEventArgs WithRoute(DragEventArgs args, RoutedEvent route) { args.RoutedEvent = route; return args; }

        try
        {
            sourceField.SetValue(window, new ApiKeyRecord { Id = one.Id, IsPinned = !two.IsPinned });
            var blocked = Drag(DragDrop.DragOverEvent, new Point(20, 20));
            target.RaiseEvent(blocked);
            Check(blocked.Effects == DragDropEffects.None && Equals(target.Tag, "Blocked") &&
                  ReferenceEquals(target.BorderBrush, ThemeManager.Brush("Danger")), "cross-section drag shows a warning and cannot drop");
            target.RaiseEvent(Drag(DragDrop.DropEvent, new Point(20, 20)));
            Check(target.Tag is null && repo.LoadApiKeys().First().Id == one.Id, "blocked drop clears feedback without changing order");
            sourceField.SetValue(window, one);
            var over = Drag(DragDrop.DragOverEvent, scroll.TranslatePoint(new Point(20, scroll.ActualHeight - 10), target));
            target.RaiseEvent(over);
            Check(Equals(target.Tag, "After") && target.BorderThickness.Bottom == 3, "allowed drag highlights the actual insertion edge");
            Pump();
            Check(over.Effects == DragDropEffects.Move && scroll.VerticalOffset > 0, "drag near bottom scrolls the active virtual list");
            target.RaiseEvent(Drag(DragDrop.DragLeaveEvent, new Point(20, 20)));
            Check(target.Tag is null, "leaving a drag target clears its recycled visual state");
            target.RaiseEvent(Drag(DragDrop.DropEvent, new Point(20, 20))); Pump();
        }
        finally { sourceField.SetValue(window, null); tokenField.SetValue(window, null); }
        Check(repo.LoadApiKeys().Take(2).Select(api => api.Id).SequenceEqual(new[] { two.Id, one.Id }), "card drop persists the reordered cards");
    }

    private static void AssertVirtualList(ItemsControl list, int minCount, string name)
    {
        var scroll = FindCardScroll(list);
        scroll.ScrollToTop();
        Pump();
        int count = list.Items.Count;
        Check(count >= minCount, $"{name} list has enough fixture rows");
        int realized = Enumerable.Range(0, count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        Check(realized > 0 && realized < count / 2, $"{name} list does not create offscreen card containers ({realized}/{count})");
        scroll.ScrollToEnd();
        WaitUi(200);
        Check(list.ItemContainerGenerator.ContainerFromIndex(count - 1) is not null, $"{name} list can scroll to its last row");
        int afterScroll = Enumerable.Range(0, count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        Check(afterScroll < count / 2, $"{name} list recycles containers on scroll ({afterScroll}/{count})");
    }
}
