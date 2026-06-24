using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Muselly.App.Views;

public partial class PlayerBarView : UserControl
{
    public PlayerBarView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
