using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        // Avalonia 12: SetTextAsync is a ClipboardExtensions extension method.
        await clipboard.SetTextAsync(vm.ShareText);
        vm.MarkDetailsCopied();
    }
}
