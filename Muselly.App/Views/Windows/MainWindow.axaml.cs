using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Muselly.App.Views.Windows;

/// <summary>
/// The desktop shell window. It wraps the shared <c>MainView</c> in custom chrome (drawn by
/// <see cref="ChromedWindow"/> on Windows/Linux, native frame on macOS) and handles the title-bar
/// drag, the window-control buttons, and — on Windows/Linux — the custom edge/corner resize handles.
/// </summary>
public partial class MainWindow : ChromedWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // --- Title-bar drag + double-click to maximise ---------------------------------------------------
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                BeginMoveDrag(e);
        }
    }

    // --- Window controls -----------------------------------------------------------------------------
    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // In fullscreen (the custom macOS green button is only shown there) this acts as "exit fullscreen".
        if (WindowState == WindowState.FullScreen)
            WindowState = WindowState.Normal;
        else
            ToggleMaximize();
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // --- Custom resize handles (Windows/Linux; the overlay is hidden on macOS) ------------------------
    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string tag }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var edge = tag switch
        {
            "Left" => WindowEdge.West,
            "Right" => WindowEdge.East,
            "Top" => WindowEdge.North,
            "Bottom" => WindowEdge.South,
            "TopLeft" => WindowEdge.NorthWest,
            "TopRight" => WindowEdge.NorthEast,
            "BottomLeft" => WindowEdge.SouthWest,
            "BottomRight" => WindowEdge.SouthEast,
            _ => (WindowEdge?)null
        };

        if (edge is { } e2)
            BeginResizeDrag(e2, e);
    }
}
