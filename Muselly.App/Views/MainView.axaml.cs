using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

/// <summary>
/// The shared application content. Hosted directly as the single-view root on browser/mobile, and
/// embedded inside <c>MainWindow</c>'s custom chrome on desktop.
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // Space toggles play/pause from anywhere, except while typing in a text field. Handled on the
        // tunnel route so it fires before a focused button would treat Space as a click.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.Handled) return;

        // Don't steal Space from editable text inputs.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox { IsReadOnly: false }) return;

        if (DataContext is MainViewModel vm && vm.Player.PlayPauseCommand.CanExecute(null))
        {
            vm.Player.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
