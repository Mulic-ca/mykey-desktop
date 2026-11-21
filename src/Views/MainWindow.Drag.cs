using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyKey.Desktop;

public partial class MainWindow
{
    private const string CardDragFormat = "MYKEY.Card";
    private Point _dragStart;
    private Button? _dragHandle;
    private Border? _dropTarget;
    private CardPreview? _dragPreview;
    private Border? _settlingCard;
    private object? _draggedCard;
    private string? _dragToken;

    private void DragHandle_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button handle || !handle.CaptureMouse()) return;
        _dragStart = e.GetPosition(this);
        _dragHandle = handle;
        e.Handled = true;
    }

    private void DragHandle_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button handle || handle != _dragHandle) return;
        _dragHandle = null;
        handle.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void DragHandle_CaptureLost(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _dragHandle)) _dragHandle = null;
    }

    private void DragHandle_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button handle || handle != _dragHandle) return;
        var distance = e.GetPosition(this) - _dragStart;
        if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragHandle = null;
        handle.ReleaseMouseCapture();
        e.Handled = true;
        if (SortSelector.SelectedIndex != 0 || _selectedTags.Count > 0 || !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            ShowToast("请清除搜索和标签筛选，并选择自定义排序");
            return;
        }
        if (handle.Tag is not (ApiKeyRecord or AccountGroupView)) return;
        var source = (DependencyObject)handle;
        while (source is not Border { AllowDrop: true } && VisualTreeHelper.GetParent(source) is { } parent) source = parent;
        if (source is not Border card) return;
        var scroll = CardScrollFor(_activeView == "api" ? ApiList : AccountList);
        var layer = scroll is null ? null : AdornerLayer.GetAdornerLayer(scroll);
        _draggedCard = handle.Tag;
        _dragToken = Guid.NewGuid().ToString("N");
        try
        {
            card.SetCurrentValue(OpacityProperty, .42);
            card.DataContextChanged += ResetDragSource;
            if (scroll is not null && layer is not null)
            {
                _dragPreview = new CardPreview(scroll, card, e.GetPosition(card));
                layer.Add(_dragPreview);
                _dragPreview.Move(e.GetPosition(scroll), false);
            }
            DragDrop.DoDragDrop(handle, new DataObject(CardDragFormat, _dragToken), DragDropEffects.Move);
        }
        finally
        {
            if (_dragPreview is { } preview) layer?.Remove(preview);
            _dragPreview = null;
            _draggedCard = null;
            _dragToken = null;
            card.DataContextChanged -= ResetDragSource;
            card.ClearValue(OpacityProperty);
            ClearDropTarget();
        }
    }

    private static void ResetDragSource(object sender, DependencyPropertyChangedEventArgs e) => ((Border)sender).ClearValue(OpacityProperty);

    private object? ReadDraggedCard(IDataObject data) => _draggedCard is not null && _dragToken is not null
        && data.GetDataPresent(CardDragFormat, false) && data.GetData(CardDragFormat, false) is string token && token == _dragToken
        ? _draggedCard : null;

    private bool MovesAfterTarget(object? source, object target) => (source, target) switch
    {
        (ApiKeyRecord a, ApiKeyRecord b) => a.CardOrder < b.CardOrder || (a.CardOrder == b.CardOrder && a.Id > b.Id),
        (AccountGroupView a, AccountGroupView b) => a.CardOrder < b.CardOrder || (a.CardOrder == b.CardOrder && a.Accounts.Max(x => x.Id) > b.Accounts.Max(x => x.Id)),
        _ => false
    };

    private void TrackDropTarget(Border target, string state)
    {
        if (!ReferenceEquals(target, _dropTarget))
        {
            ClearDropTarget();
            _dropTarget = target;
            target.DataContextChanged += DropTargetChanged;
            target.Unloaded += DropTargetUnloaded;
        }
        if (!Equals(target.Tag, state)) target.Tag = state;
    }

    private void DropTargetChanged(object sender, DependencyPropertyChangedEventArgs e) => ClearDropTarget();
    private void DropTargetUnloaded(object sender, RoutedEventArgs e) => ClearDropTarget();
    private void Card_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, _dropTarget)) ClearDropTarget();
    }

    private void CardArea_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (ReadDraggedCard(e.Data) is null) return;
        if (CardScrollFor(_activeView == "api" ? ApiList : AccountList) is not { } scroll) return;
        for (var hit = scroll.InputHitTest(e.GetPosition(scroll)) as DependencyObject;
             hit is Visual; hit = VisualTreeHelper.GetParent(hit))
            if (hit is Border { AllowDrop: true }) return; // The card's own handler supplies the feedback.
        ClearDropTarget();
        _dragPreview?.Move(e.GetPosition(scroll), false);
    }

    private void ClearDropTarget()
    {
        if (_dropTarget is not { } target) return;
        _dropTarget = null;
        target.DataContextChanged -= DropTargetChanged;
        target.Unloaded -= DropTargetUnloaded;
        target.ClearValue(TagProperty);
    }

    private void AnimateDroppedCard(object? source)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            static bool SameCard(object? a, object? b) => (a, b) switch
            {
                (ApiKeyRecord x, ApiKeyRecord y) => x.Id == y.Id,
                (AccountGroupView x, AccountGroupView y) => x.Key == y.Key,
                _ => false
            };
            static Border? Find(DependencyObject node, object? card)
            {
                if (node is Border { AllowDrop: true } border && SameCard(border.DataContext, card)) return border;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Find(VisualTreeHelper.GetChild(node, i), card) is { } found) return found;
                return null;
            }
            if (Find(_activeView == "api" ? ApiList : AccountList, source) is not { } card) return;
            if (_settlingCard is { } prior) ClearSettle(prior);
            _settlingCard = card;
            var transform = new TranslateTransform();
            card.RenderTransform = transform;
            card.DataContextChanged += SettleContextChanged;
            var motion = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(155)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            motion.Completed += (_, _) => { if (ReferenceEquals(card.RenderTransform, transform)) ClearSettle(card); };
            transform.BeginAnimation(TranslateTransform.YProperty, motion);
        });
    }

    private void SettleContextChanged(object sender, DependencyPropertyChangedEventArgs e) => ClearSettle((Border)sender);
    private void ClearSettle(Border card)
    {
        if (ReferenceEquals(_settlingCard, card)) _settlingCard = null;
        card.DataContextChanged -= SettleContextChanged;
        card.ClearValue(RenderTransformProperty);
    }

    private sealed class CardPreview : Adorner
    {
        private readonly Border _preview;
        private readonly Point _anchor;
        private readonly TranslateTransform _position = new();

        internal CardPreview(ScrollViewer viewport, Border card, Point anchor) : base(viewport)
        {
            _anchor = anchor;
            IsHitTestVisible = false;
            ClipToBounds = true;
            var title = card.DataContext is ApiKeyRecord api ? api.Name : ((AccountGroupView)card.DataContext).WebsiteTitle;
            _preview = new Border
            {
                Width = Math.Min(card.ActualWidth, 420), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                Background = ThemeManager.Brush("Panel"), BorderBrush = ThemeManager.Brush("Accent"),
                Padding = new Thickness(16, 10, 16, 10), RenderTransform = _position,
                Child = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = ThemeManager.Brush("Ink"), FontWeight = FontWeights.SemiBold, FontSize = 16, FontFamily = card.Child is FrameworkElement content
                        ? (FontFamily)content.GetValue(TextBlock.FontFamilyProperty) : SystemFonts.MessageFontFamily }
            };
            AddVisualChild(_preview);
            if (SystemParameters.ClientAreaAnimation)
                _preview.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(100)));
        }

        internal void Move(Point pointer, bool canDrop)
        {
            _position.X = Math.Clamp(pointer.X - Math.Min(_anchor.X, _preview.Width - 18), 0, Math.Max(0, AdornedElement.RenderSize.Width - _preview.Width));
            _position.Y = Math.Clamp(pointer.Y - 52, 0, Math.Max(0, AdornedElement.RenderSize.Height - 48));
            _preview.BorderBrush = ThemeManager.Brush(canDrop ? "Accent" : "Danger");
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? _preview : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size constraint) { _preview.Measure(constraint); return AdornedElement.RenderSize; }
        protected override Size ArrangeOverride(Size size) { _preview.Arrange(new Rect(_preview.DesiredSize)); return size; }
    }
}
