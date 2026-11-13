using System.Collections;
using System.Collections.Specialized;

namespace HomeManagementClient.Controls;

public sealed class StaticItemsView : ContentView
{
    private const double Spacing = 12;

    private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(50);

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(StaticItemsView),
            propertyChanged: (bindable, oldValue, newValue) => ((StaticItemsView)bindable).OnItemsSourceChanged(oldValue as IEnumerable, newValue as IEnumerable));

    public static readonly BindableProperty VersionProperty =
        BindableProperty.Create(nameof(Version), typeof(int), typeof(StaticItemsView),
            propertyChanged: (bindable, _, _) => ((StaticItemsView)bindable).ScheduleRebuild());

    private bool _isRebuildScheduled;
    private int _builtColumns;

    public StaticItemsView()
    {
        SizeChanged += OnSizeChanged;
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public DataTemplate? ItemTemplate { get; set; }

    public string EmptyText { get; set; } = string.Empty;

    public double MinItemWidth { get; set; }

    public double BottomPadding { get; set; } = 16;

    public static StaticItemsView Replace(CollectionView collectionView, string emptyText)
    {
        var refreshView = (RefreshView)collectionView.Parent;
        var items = new StaticItemsView { ItemTemplate = collectionView.ItemTemplate, EmptyText = emptyText };
        refreshView.Content = new ScrollView { Content = items };
        return items;
    }

    private void OnItemsSourceChanged(IEnumerable? oldItems, IEnumerable? newItems)
    {
        if (oldItems is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnCollectionChanged;
        }

        if (newItems is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnCollectionChanged;
        }

        ScheduleRebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRebuild();

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (MinItemWidth > 0 && GetColumns(Items().Count) != _builtColumns)
        {
            ScheduleRebuild();
        }
    }

    private void ScheduleRebuild()
    {
        if (_isRebuildScheduled)
        {
            return;
        }

        _isRebuildScheduled = true;
        Dispatcher.DispatchDelayed(RebuildDelay, () =>
        {
            _isRebuildScheduled = false;
            Rebuild();
        });
    }

    private List<object> Items() => ItemsSource?.Cast<object>().ToList() ?? [];

    private int GetColumns(int itemCount)
    {
        if (MinItemWidth <= 0 || Width <= 0)
        {
            return 1;
        }

        var fitting = (int)((Width + Spacing) / (MinItemWidth + Spacing));
        return Math.Clamp(fitting, 1, Math.Max(1, itemCount));
    }

    private void Rebuild()
    {
        var items = Items();
        var views = new List<View>();
        foreach (var item in items)
        {
            var template = ItemTemplate is DataTemplateSelector selector ? selector.SelectTemplate(item, this) : ItemTemplate;
            if (template?.CreateContent() is View view)
            {
                view.BindingContext = item;
                views.Add(view);
            }
        }

        _builtColumns = GetColumns(views.Count);
        var padding = new Thickness(0, 0, 0, BottomPadding);
        if (views.Count == 0)
        {
            Content = new Label
            {
                Text = Version == 0 ? "Loading..." : EmptyText,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 48)
            };
            return;
        }

        if (_builtColumns == 1)
        {
            var stack = new VerticalStackLayout { Spacing = Spacing, Padding = padding };
            foreach (var view in views)
            {
                stack.Add(view);
            }

            Content = stack;
            return;
        }

        var grid = new Grid { ColumnSpacing = Spacing, RowSpacing = Spacing, Padding = padding };
        for (var i = 0; i < _builtColumns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var i = 0; i < (views.Count + _builtColumns - 1) / _builtColumns; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < views.Count; i++)
        {
            grid.Add(views[i], i % _builtColumns, i / _builtColumns);
        }

        Content = grid;
    }
}
