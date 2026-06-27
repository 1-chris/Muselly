using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Muselly.App.Controls;

/// <summary>
/// Remembers a scroll position across navigation. Attach <c>ScrollMemory.Key="…"</c> to a
/// <see cref="ScrollViewer"/> (or any control that contains one, e.g. a <see cref="ListBox"/>); the offset is
/// saved live as the user scrolls and restored when the same key reappears. So scrolling a long list, opening
/// an item, then pressing Back lands exactly where you were. Offsets are kept in-memory for the session,
/// keyed by the supplied string (use a stable per-page key, e.g. "lib.albums" or "album/{key}").
/// </summary>
public static class ScrollMemory
{
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Key", typeof(ScrollMemory));

    public static void SetKey(Control control, string? value) => control.SetValue(KeyProperty, value);
    public static string? GetKey(Control control) => control.GetValue(KeyProperty);

    private static readonly Dictionary<string, Vector> Offsets = new();
    private static readonly ConditionalWeakTable<Control, State> States = new();

    private sealed class State
    {
        public bool Attached;
        public ScrollViewer? Scroller;
        public EventHandler<ScrollChangedEventArgs>? OnScroll;
        public EventHandler? OnLayout;
    }

    static ScrollMemory() => KeyProperty.Changed.AddClassHandler<Control>(OnKeyChanged);

    private static void OnKeyChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= OnAttached;
        control.DetachedFromVisualTree -= OnDetached;
        if (string.IsNullOrEmpty(GetKey(control))) return;

        control.AttachedToVisualTree += OnAttached;
        control.DetachedFromVisualTree += OnDetached;
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control c) return;
        States.GetValue(c, _ => new State()).Attached = true;
        Hook(c);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control c || !States.TryGetValue(c, out var state)) return;
        state.Attached = false;
        if (state.Scroller is not { } sv) return;

        var key = GetKey(c);
        if (!string.IsNullOrEmpty(key)) Offsets[key!] = sv.Offset; // capture the final position before unloading
        if (state.OnScroll is not null) sv.ScrollChanged -= state.OnScroll;
        if (state.OnLayout is not null) sv.LayoutUpdated -= state.OnLayout;
        state.Scroller = null;
        state.OnScroll = null;
        state.OnLayout = null;
    }

    private static void Hook(Control control)
    {
        // Defer so a ListBox/TreeView has realised its inner ScrollViewer by the time we look for it.
        Dispatcher.UIThread.Post(() =>
        {
            var key = GetKey(control);
            if (string.IsNullOrEmpty(key)) return;
            var state = States.GetValue(control, _ => new State());
            if (!state.Attached) return;

            var scroller = control as ScrollViewer ?? control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is null) return;

            if (ReferenceEquals(state.Scroller, scroller)) return; // already wired
            state.Scroller = scroller;

            state.OnScroll = (_, _) =>
            {
                var k = GetKey(control);
                if (!string.IsNullOrEmpty(k)) Offsets[k!] = scroller.Offset;
            };
            scroller.ScrollChanged += state.OnScroll;

            if (Offsets.TryGetValue(key!, out var target) && (target.X > 0 || target.Y > 0))
                RestoreOffset(scroller, target, state);
        }, DispatcherPriority.Loaded);
    }

    private static void RestoreOffset(ScrollViewer scroller, Vector target, State state)
    {
        // The content's extent isn't known until it lays out (and a virtualised/paged list may grow over a
        // few passes), so re-apply on each layout until we reach the target — bounded by a short deadline.
        var deadline = DateTime.UtcNow.AddSeconds(2);

        EventHandler? layout = null;
        layout = (_, _) =>
        {
            var maxY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            var maxX = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            var y = Math.Min(target.Y, maxY);
            var x = Math.Min(target.X, maxX);
            if (Math.Abs(scroller.Offset.Y - y) > 0.5 || Math.Abs(scroller.Offset.X - x) > 0.5)
                scroller.Offset = new Vector(x, y);

            if (y >= target.Y - 0.5 || DateTime.UtcNow > deadline)
            {
                scroller.LayoutUpdated -= layout!;
                if (state.OnLayout == layout) state.OnLayout = null;
            }
        };
        state.OnLayout = layout;
        scroller.LayoutUpdated += layout;
        layout(scroller, EventArgs.Empty); // apply once immediately
    }
}
