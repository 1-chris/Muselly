using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Muselly.Web.Views;

public partial class WebShell : UserControl
{
    public WebShell() => AvaloniaXamlLoader.Load(this);
}
