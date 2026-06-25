using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class ArtistDetailView : UserControl
{
    public ArtistDetailView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAlbumMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArtistDetailViewModel vm) return;
        if (sender is not MenuItem item) return;
        if ((item.Tag as string) == "album.play") vm.PlayAlbumCommand.Execute(item.DataContext);
    }
}
