using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class AlbumDetailView : UserControl
{
    public AlbumDetailView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnTrackMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AlbumDetailViewModel vm) return;
        if (sender is not MenuItem item) return;
        var data = item.DataContext;
        switch (item.Tag as string)
        {
            case "track.play": vm.PlayTrackCommand.Execute(data); break;
            case "track.playnext": vm.PlayTrackNextCommand.Execute(data); break;
            case "track.queue": vm.AddTrackToQueueCommand.Execute(data); break;
        }
    }
}
