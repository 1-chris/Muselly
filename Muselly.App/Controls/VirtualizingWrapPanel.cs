using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace Muselly.App.Controls;

/// <summary>
/// A virtualizing uniform-grid (wrap) panel: lays items out left-to-right, wrapping into rows of fixed-size
/// cells, and only realizes the cells currently visible in the enclosing <see cref="ScrollViewer"/> — cells
/// scrolled out of view are recycled (their controls reused for new items). This keeps both the realized
/// control count and, together with the bounded artwork cache, memory flat on huge libraries. Avalonia 12
/// ships no virtualizing wrap layout, so this is our own.
///
/// Usage: place inside a (vertical) <see cref="ScrollViewer"/> and set <see cref="ItemsSource"/> +
/// <see cref="ItemTemplate"/> and the cell size.
/// </summary>
public sealed class VirtualizingWrapPanel : Panel
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemWidth), 184);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHeight), 232);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ColumnSpacing), 4);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(RowSpacing), 6);

    private readonly Dictionary<int, Control> _realized = new();
    private readonly Stack<Control> _recycled = new();
    private IList? _items;
    private INotifyCollectionChanged? _incc;
    private ScrollViewer? _scroll;
    private int _columns = 1;

    static VirtualizingWrapPanel()
    {
        AffectsMeasure<VirtualizingWrapPanel>(ItemWidthProperty, ItemHeightProperty,
            ColumnSpacingProperty, RowSpacingProperty, ItemTemplateProperty);
    }

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public double ItemWidth { get => GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
            OnItemsSourceChanged(change.GetNewValue<IEnumerable?>());
        else if (change.Property == ItemTemplateProperty)
            ResetContainers();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null) _scroll.PropertyChanged += OnScrollPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroll is not null) _scroll.PropertyChanged -= OnScrollPropertyChanged;
        _scroll = null;
    }

    private void OnScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ViewportProperty)
            InvalidateMeasure();
    }

    private void OnItemsSourceChanged(IEnumerable? value)
    {
        if (_incc is not null) _incc.CollectionChanged -= OnCollectionChanged;
        _items = value as IList ?? value?.Cast<object?>().ToList();
        _incc = value as INotifyCollectionChanged;
        if (_incc is not null) _incc.CollectionChanged += OnCollectionChanged;
        ResetContainers();
        InvalidateMeasure();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The library resets these collections wholesale (filter/scan), so re-realize from scratch.
        if (_items is null && ItemsSource is { } src) _items = src as IList ?? src.Cast<object?>().ToList();
        ResetContainers();
        InvalidateMeasure();
    }

    private void ResetContainers()
    {
        _realized.Clear();
        _recycled.Clear();
        Children.Clear();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = _items?.Count ?? 0;
        if (count == 0 || ItemTemplate is null)
        {
            RecycleRange(int.MinValue, int.MinValue); // recycle everything
            return default;
        }

        var cellW = ItemWidth + ColumnSpacing;
        var cellH = ItemHeight + RowSpacing;

        var width = availableSize.Width;
        if (double.IsInfinity(width) || width <= 0) width = _scroll?.Viewport.Width ?? 0;
        if (width <= 0) width = 1000; // pre-layout fallback

        _columns = System.Math.Max(1, (int)((width + ColumnSpacing) / cellW));
        var rows = (count + _columns - 1) / _columns;
        var totalHeight = rows * cellH;

        // Visible row range from the scroll viewport (with a one-row buffer on each side).
        var top = _scroll?.Offset.Y ?? 0;
        var viewportH = _scroll?.Viewport.Height ?? 0;
        if (viewportH <= 0) viewportH = 1200; // first pass before the viewport is known
        var firstRow = System.Math.Max(0, (int)(top / cellH) - 1);
        var lastRow = System.Math.Min(rows - 1, (int)((top + viewportH) / cellH) + 1);

        var first = firstRow * _columns;
        var last = System.Math.Min(count - 1, (lastRow + 1) * _columns - 1);

        RecycleRange(first, last);

        var cellSize = new Size(ItemWidth, ItemHeight);
        for (var i = first; i <= last; i++)
        {
            if (!_realized.TryGetValue(i, out var container))
            {
                container = GetContainer(_items![i]);
                _realized[i] = container;
            }
            container.Measure(cellSize);
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cellStepX = ItemWidth + ColumnSpacing;
        var cellStepY = ItemHeight + RowSpacing;
        foreach (var (index, container) in _realized)
        {
            var col = index % _columns;
            var row = index / _columns;
            container.Arrange(new Rect(col * cellStepX, row * cellStepY, ItemWidth, ItemHeight));
        }
        return finalSize;
    }

    /// <summary>Recycles realized containers whose index falls outside [first, last].</summary>
    private void RecycleRange(int first, int last)
    {
        List<int>? drop = null;
        foreach (var index in _realized.Keys)
            if (index < first || index > last)
                (drop ??= new List<int>()).Add(index);

        if (drop is null) return;
        foreach (var index in drop)
        {
            var container = _realized[index];
            _realized.Remove(index);
            container.IsVisible = false;
            container.DataContext = null;
            _recycled.Push(container);
        }
    }

    private Control GetContainer(object? item)
    {
        Control container;
        if (_recycled.Count > 0)
        {
            container = _recycled.Pop();
            container.IsVisible = true;
        }
        else
        {
            container = ItemTemplate!.Build(item) ?? new Control();
            Children.Add(container);
        }
        container.DataContext = item;
        return container;
    }
}
