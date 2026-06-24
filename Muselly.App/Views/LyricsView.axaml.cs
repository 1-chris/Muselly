using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class LyricsView : UserControl
{
    private ListBox? _lyricsList;
    private ScrollViewer? _scrollViewer;

    public LyricsView()
    {
        InitializeComponent();
        _lyricsList = this.FindControl<ListBox>("LyricsList");
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private LyricsViewModel? _vm;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as LyricsViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.ActiveIndex)) return;
        ScrollActiveIntoView();
    }

    private void ScrollActiveIntoView()
    {
        if (_vm is null || _vm.ActiveIndex < 0 || _lyricsList is null) return;
        var index = _vm.ActiveIndex;
        // Defer so the container is realised, then centre the active line in the viewport.
        Dispatcher.UIThread.Post(() => CenterOnIndex(index), DispatcherPriority.Background);
    }

    private void CenterOnIndex(int index)
    {
        if (_lyricsList is null || index < 0 || index >= _lyricsList.ItemCount) return;

        // Make sure the line is at least realised so we can measure it.
        _lyricsList.ScrollIntoView(index);

        _scrollViewer ??= _lyricsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var sv = _scrollViewer;
        var container = _lyricsList.ContainerFromIndex(index);
        if (sv is null || container is null) return;

        var pos = container.TranslatePoint(new Point(0, 0), sv);
        if (pos is null) return;

        // Position within the scrollable content, then offset so the line sits in the middle.
        var contentY = pos.Value.Y + sv.Offset.Y;
        var target = contentY - (sv.Viewport.Height / 2) + (container.Bounds.Height / 2);
        var maxY = System.Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        target = System.Math.Clamp(target, 0, maxY);

        sv.Offset = new Vector(sv.Offset.X, target);
    }
}
