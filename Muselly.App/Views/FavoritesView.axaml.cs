using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Muselly.App.Views;

public partial class FavoritesView : UserControl
{
    public FavoritesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
