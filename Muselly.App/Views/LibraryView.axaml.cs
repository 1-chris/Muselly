using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class LibraryView : UserControl
{
    // Start loading the next page before the user actually hits the bottom, so scrolling feels seamless.
    private const double LoadThreshold = 800;

    public LibraryView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAlbumsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && DataContext is LibraryViewModel vm && IsNearBottom(sv))
            vm.LoadMoreAlbums();
    }

    private void OnArtistsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && DataContext is LibraryViewModel vm && IsNearBottom(sv))
            vm.LoadMoreArtists();
    }

    private static bool IsNearBottom(ScrollViewer sv) =>
        sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - LoadThreshold;

    // Context menus run in a separate popup tree, so XAML RelativeSource/PlacementTarget bindings to the
    // page view model are unreliable. We dispatch from code-behind instead: the view's DataContext is the
    // page VM, and the clicked item is the menu item's (inherited) DataContext.
    private void OnLibraryMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var canShare = (DataContext as LibraryViewModel)?.CanShare ?? false;
        foreach (var entry in menu.Items)
            if (entry is Control control && control.Tag is string tag &&
                (tag == "share-sep" || tag.EndsWith(".share")))
                control.IsVisible = canShare;
    }

    private void OnLibraryMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;
        if (sender is not MenuItem item) return;
        var data = item.DataContext;

        switch (item.Tag as string)
        {
            case "album.play": vm.PlayAlbumCommand.Execute(data); break;
            case "album.playnext": vm.PlayNextAlbumCommand.Execute(data); break;
            case "album.shuffle": vm.PlayAlbumShuffledCommand.Execute(data); break;
            case "album.queue": vm.QueueAlbumCommand.Execute(data); break;
            case "album.queueshuffle": vm.QueueAlbumShuffledCommand.Execute(data); break;
            case "album.share": vm.ShareAlbumCommand.Execute(data); break;

            case "artist.play": vm.PlayArtistCommand.Execute(data); break;
            case "artist.playnext": vm.PlayNextArtistCommand.Execute(data); break;
            case "artist.shuffle": vm.PlayArtistShuffledCommand.Execute(data); break;
            case "artist.queue": vm.QueueArtistCommand.Execute(data); break;
            case "artist.queueshuffle": vm.QueueArtistShuffledCommand.Execute(data); break;
            case "artist.share": vm.ShareArtistCommand.Execute(data); break;

            case "song.play": vm.PlaySongCommand.Execute(data); break;
            case "song.playnext": vm.QueueSongNextCommand.Execute(data); break;
            case "song.queue": vm.QueueSongCommand.Execute(data); break;
            case "song.share": vm.ShareSongCommand.Execute(data); break;
        }
    }
}
