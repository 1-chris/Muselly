using System;
using Avalonia;
using Avalonia.Controls;
using Muselly.App.Platform;

namespace Muselly.App.Views.Windows;

/// <summary>
/// Shared custom-chrome behaviour for the app's borderless windows.
///
/// On Windows/Linux the window is fully self-drawn (no system decorations): the rounded
/// <c>RootBorder</c> is squared off while maximised, and the right-side Windows/Linux button group
/// is used.
///
/// On macOS we instead hand the frame back to the OS (<c>WindowDecorations.Full</c> +
/// <c>ExtendClientAreaToDecorationsHint</c>): macOS draws the real traffic lights over our extended
/// client area, and — crucially — the green button performs a true native fullscreen (its own Space,
/// the slide animation, the auto-hiding menu bar) instead of merely filling the desktop. The custom
/// blob group is reduced to a fixed-width spacer so the title content clears the real lights, and the
/// inner border stays square/borderless because the native frame already supplies corners + shadow.
///
/// Windows opt in purely by naming controls in their XAML (no per-window code needed):
///   • the outer rounded <c>Border</c> as <c>RootBorder</c>;
///   • optionally the left-side macOS blob group as <c>MacWindowButtons</c>;
///   • optionally the right-side Windows/Linux group as <c>StandardWindowButtons</c>;
///   • optionally the custom resize-handle overlay as <c>ResizeHandles</c>.
/// </summary>
public abstract class ChromedWindow : Window
{
    private static bool UseMacChrome => OperatingSystem.IsMacOS(); // || OperatingSystem.IsLinux();

    /// <summary>
    /// Width reserved at the left of the title bar for the native macOS traffic lights. Combined with
    /// the <c>MacWindowButtons</c> panel's own left margin this clears the real (close/min/zoom) blobs.
    /// </summary>
    private const double MacTrafficLightInset = 64;

    private Border? _rootBorder;
    private Control? _macWindowButtons;

    protected ChromedWindow()
    {
        // Decide the frame style *before* the XAML loads (this ctor runs before the derived window's
        // InitializeComponent), so the window is never the wrong kind even briefly.
        //
        // macOS: keep the OS-drawn frame the window is created with (WindowDecorations.Full) — that's
        // what gives the real traffic lights AND native fullscreen via the green button. We must NOT
        // route it through None: toggling the NSWindow style mask to borderless drops the standard
        // window buttons, and switching back to Full does not reliably restore them. The XAML no
        // longer sets SystemDecorations, so the macOS default sticks.
        //
        // Windows/Linux: go borderless and draw our own chrome, as before.
        if (!UseMacChrome)
            WindowDecorations = WindowDecorations.None;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _rootBorder = this.FindControl<Border>("RootBorder");

        _macWindowButtons = this.FindControl<Control>("MacWindowButtons");
        if (_macWindowButtons is { } mac && !UseMacChrome)
            mac.IsVisible = false;

        // macOS window-control handling (see UpdateMacWindowControls): native lights when windowed, our own
        // when fullscreen. Guarded with OperatingSystem.IsMacOS() so the analyzer sees the interop as safe.
        if (UseMacChrome)
            UpdateMacWindowControls();

        if (this.FindControl<Control>("StandardWindowButtons") is { } standard)
            standard.IsVisible = !UseMacChrome;

        // The native frame handles edge/corner resize on macOS, so the custom overlay handles
        // (which would otherwise sit on top and swallow the native resize cursors) are dropped.
        if (this.FindControl<Control>("ResizeHandles") is { } resize)
            resize.IsVisible = !UseMacChrome;

        UpdateMaximizedChrome();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizedChrome();
            if (UseMacChrome)
            {
                UpdateMacWindowControls();
                if (OperatingSystem.IsMacOS() && WindowState != WindowState.FullScreen)
                    ReassertExtendedClientArea();
            }
        }
    }

    private void ReassertExtendedClientArea()
    {
        if (!OperatingSystem.IsMacOS()) return;

        // macOS restores the standard title bar at the *end* of the exit-fullscreen animation, so a single
        // immediate call gets overwritten. Re-assert our transparent/extended title bar immediately and a
        // couple of times after the animation settles. These set the same stable state, so there's no flicker.
        MacTitleBar.ApplyExtendedClientArea(this);
#pragma warning disable CA1416 // guarded by the IsMacOS() check above; the analyzer can't see it in the lambdas
        foreach (var seconds in new[] { 0.35, 0.7, 1.2 })
            Avalonia.Threading.DispatcherTimer.RunOnce(
                () => MacTitleBar.ApplyExtendedClientArea(this), TimeSpan.FromSeconds(seconds));
#pragma warning restore CA1416
    }

    /// <summary>
    /// Swaps the macOS window controls between two modes:
    ///   • Windowed/maximised — the real native traffic lights (drawn by macOS over the extended client
    ///     area); our <c>MacWindowButtons</c> panel is just a fixed-width spacer so the title starts clear
    ///     of them.
    ///   • Fullscreen — the native lights are hidden (macOS would otherwise auto-hide them with the title
    ///     bar, and fling them to the wrong corner on every title-bar event), and our own traffic-light
    ///     buttons are shown in their place, where they stay put.
    /// </summary>
    private void UpdateMacWindowControls()
    {
        if (!UseMacChrome || _macWindowButtons is not { } mac) return;

        var fullScreen = WindowState == WindowState.FullScreen;

        mac.IsVisible = true;
        if (mac is Panel panel)
            foreach (var child in panel.Children)
                child.IsVisible = fullScreen;            // our blobs are real buttons in fullscreen, hidden (spacer) otherwise
        mac.Width = fullScreen ? double.NaN : MacTrafficLightInset;

        if (OperatingSystem.IsMacOS())
            MacTitleBar.SetNativeButtonsHidden(this, fullScreen);
    }

    private void UpdateMaximizedChrome()
    {
        if (_rootBorder is null) return;

        // On macOS the native frame supplies the rounded corners, border and shadow, so the inner
        // border must stay square and borderless to avoid a doubled-up edge.
        if (UseMacChrome)
        {
            _rootBorder.CornerRadius = new CornerRadius(0);
            _rootBorder.BorderThickness = new Thickness(0);
            return;
        }

        // Elsewhere, square off the radius while maximised — it would otherwise expose transparent
        // gaps against the screen edges.
        _rootBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(8);
    }
}
