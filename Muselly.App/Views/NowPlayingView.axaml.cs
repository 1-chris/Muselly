using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Muselly.App.Views;

public partial class NowPlayingView : UserControl
{
    public NowPlayingView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
