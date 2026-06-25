using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnPlaylistMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var canShare = (DataContext as PlaylistsViewModel)?.CanShare ?? false;
        foreach (var entry in menu.Items)
            if (entry is Control control && control.Tag is string tag && tag.EndsWith(".share"))
                control.IsVisible = canShare;
    }

    private void OnPlaylistMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel vm) return;
        if (sender is not MenuItem item) return;
        if ((item.Tag as string) == "playlist.share") vm.ShareCommand.Execute(item.DataContext);
    }
}
