using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Muselly.App.Views;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
