using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Muselly.Core.Models;

namespace Muselly.App.Controls;

/// <summary>The payload carried by a drag from the library or within the queue.</summary>
public sealed class TrackDragPayload
{
    public required IReadOnlyList<Track> Tracks { get; init; }

    /// <summary>A human label for the drag (e.g. the album/song name).</summary>
    public string? Label { get; init; }

    /// <summary>When dragging an existing queue item, its index in the queue; otherwise -1.</summary>
    public int SourceQueueIndex { get; init; } = -1;
}

/// <summary>Identifies where a drag originated, which determines the payload and allowed effect.</summary>
public enum DragSourceKind
{
    None,
    /// <summary>Library lists/cards: tracks, albums and artists can be dragged (copy into the queue).</summary>
    Library,
    /// <summary>The queue list: an item is dragged to reorder it (move).</summary>
    Queue
}

/// <summary>
/// Attached behaviour that turns an <see cref="ItemsControl"/> (a track list, the queue, or an album/artist
/// grid) into a drag source. The payload is built from the data context of the item under the pointer:
/// a <see cref="Track"/> becomes one track, an <see cref="Album"/>/<see cref="Artist"/> expands to all of
/// its tracks. The queue variant additionally records the dragged item's index so it can be reordered. The
/// queue itself wires up the drop side (see <c>QueueView</c>).
/// </summary>
public static class DragDropBehavior
{
    private const double DragThreshold = 5;

    /// <summary>The in-process data format used to carry a <see cref="TrackDragPayload"/> across a drag.</summary>
    public static readonly DataFormat<TrackDragPayload> PayloadFormat =
        DataFormat.CreateInProcessFormat<TrackDragPayload>("muselly/track-list");

    public static readonly AttachedProperty<DragSourceKind> DragSourceProperty =
        AvaloniaProperty.RegisterAttached<Control, DragSourceKind>("DragSource", typeof(DragDropBehavior));

    public static void SetDragSource(Control control, DragSourceKind value) => control.SetValue(DragSourceProperty, value);
    public static DragSourceKind GetDragSource(Control control) => control.GetValue(DragSourceProperty);

    private sealed class DragState
    {
        public Point Origin;
        public bool Pressed;
        public PointerPressedEventArgs? PressedArgs;
        public IReadOnlyList<Track>? Tracks;
        public string? Label;
        public int QueueIndex = -1;
    }

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    static DragDropBehavior()
    {
        DragSourceProperty.Changed.AddClassHandler<Control>(OnDragSourceChanged);
    }

    private static void OnDragSourceChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);

        if (e.NewValue is DragSourceKind kind && kind != DragSourceKind.None)
        {
            // Listen on the tunnel (preview) route with handledEventsToo so we still detect the drag even
            // when an item is a Button (which captures the pointer and marks the move as handled).
            const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, routes, handledEventsToo: true);
            control.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, routes, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, routes, handledEventsToo: true);
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (IsInteractive(e.Source as Visual, control)) return;

        var kind = GetDragSource(control);
        var (tracks, label) = ResolveTracks(e.Source as Visual, control);
        if (tracks is null || tracks.Count == 0) return;

        var state = States.GetOrCreateValue(control);
        state.Origin = e.GetPosition(control);
        state.Pressed = true;
        state.PressedArgs = e;
        state.Tracks = tracks;
        state.Label = label;
        state.QueueIndex = kind == DragSourceKind.Queue ? ResolveQueueIndex(e.Source as Visual, control) : -1;
    }

    private static async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control) return;
        if (!States.TryGetValue(control, out var state) || !state.Pressed || state.Tracks is null || state.PressedArgs is null) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) { state.Pressed = false; return; }

        var pos = e.GetPosition(control);
        if (Math.Abs(pos.X - state.Origin.X) < DragThreshold && Math.Abs(pos.Y - state.Origin.Y) < DragThreshold)
            return;

        var payload = new TrackDragPayload
        {
            Tracks = state.Tracks,
            Label = state.Label,
            SourceQueueIndex = state.QueueIndex
        };
        var kind = GetDragSource(control);
        var pressedArgs = state.PressedArgs;
        state.Pressed = false;
        state.PressedArgs = null;

        var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(PayloadFormat, payload);
        // macOS requires at least one native pasteboard item, otherwise beginning the drag throws
        // ("0 items on the pasteboard, but 1 drag images"). A text format provides that item.
        item.SetText(payload.Label ?? "tracks");
        transfer.Add(item);

        var effects = kind == DragSourceKind.Queue ? DragDropEffects.Move : DragDropEffects.Copy;
        try
        {
            await DragDrop.DoDragDropAsync(pressedArgs, transfer, effects);
        }
        catch
        {
            /* drag cancelled or failed — ignore */
        }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control control && States.TryGetValue(control, out var state))
        {
            state.Pressed = false;
            state.PressedArgs = null;
        }
    }

    private static (IReadOnlyList<Track>?, string?) ResolveTracks(Visual? source, Control root)
    {
        var visual = source;
        while (visual is not null)
        {
            if (visual is StyledElement styled)
            {
                switch (styled.DataContext)
                {
                    case Track t:
                        return (new[] { t }, t.Title);
                    case Album a:
                        return (a.Tracks, a.Title);
                    case Artist ar:
                        return (ar.Tracks, ar.Name);
                }
            }
            if (ReferenceEquals(visual, root)) break;
            visual = visual.GetVisualParent();
        }
        return (null, null);
    }

    private static int ResolveQueueIndex(Visual? source, Control root)
    {
        if (root is not ItemsControl items) return -1;
        var visual = source;
        while (visual is not null)
        {
            if (visual is Control c)
            {
                var index = items.IndexFromContainer(c);
                if (index >= 0) return index;
            }
            if (ReferenceEquals(visual, root)) break;
            visual = visual.GetVisualParent();
        }
        return -1;
    }

    private static bool IsInteractive(Visual? source, Control root)
    {
        // Only block text/range inputs. Buttons are fine: a plain click still fires because a drag only
        // starts once the pointer moves past the threshold, so dragging never steals a click.
        var visual = source;
        while (visual is not null && !ReferenceEquals(visual, root))
        {
            if (visual is Slider or TextBox) return true;
            visual = visual.GetVisualParent();
        }
        return false;
    }
}
