using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Muselly.App.Controls;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class QueueView : UserControl
{
    private Border? _dropIndicator;
    private ListBox? _queueList;

    public QueueView()
    {
        InitializeComponent();

        _dropIndicator = this.FindControl<Border>("DropIndicator");
        _queueList = this.FindControl<ListBox>("QueueList");

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var payload = e.DataTransfer.TryGetValue(DragDropBehavior.PayloadFormat);
        if (payload is null)
        {
            e.DragEffects = DragDropEffects.None;
            if (_dropIndicator is not null) _dropIndicator.IsVisible = false;
            return;
        }

        e.DragEffects = payload.SourceQueueIndex >= 0 ? DragDropEffects.Move : DragDropEffects.Copy;

        var (_, y) = ComputeInsertion(e.GetPosition(this));
        if (_dropIndicator is not null)
        {
            _dropIndicator.Margin = new Thickness(0, Math.Max(0, y), 0, 0);
            _dropIndicator.IsVisible = true;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_dropIndicator is not null) _dropIndicator.IsVisible = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_dropIndicator is not null) _dropIndicator.IsVisible = false;
        if (DataContext is not QueueViewModel vm) return;
        if (e.DataTransfer.TryGetValue(DragDropBehavior.PayloadFormat) is not { } payload) return;

        var (index, _) = ComputeInsertion(e.GetPosition(this));

        if (payload.SourceQueueIndex >= 0)
            vm.MoveTrack(payload.SourceQueueIndex, index);
        else
            vm.InsertTracks(index, payload.Tracks);

        e.Handled = true;
    }

    /// <summary>
    /// Works out where a drop should land: the queue index to insert before, and the Y (relative to this
    /// view) at which to draw the indicator line.
    /// </summary>
    private (int Index, double Y) ComputeInsertion(Point pointer)
    {
        var list = _queueList;
        if (list is null || !list.IsVisible)
            return (0, 0);

        var rows = new List<(int Index, double Top, double Bottom)>();
        foreach (var container in list.GetRealizedContainers())
        {
            var idx = list.IndexFromContainer(container);
            if (idx < 0) continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), this);
            if (topLeft is null) continue;
            var top = topLeft.Value.Y;
            rows.Add((idx, top, top + container.Bounds.Height));
        }

        if (rows.Count == 0)
            return (0, 0);

        rows.Sort((a, b) => a.Top.CompareTo(b.Top));

        foreach (var row in rows)
        {
            var mid = (row.Top + row.Bottom) / 2;
            if (pointer.Y < mid)
                return (row.Index, row.Top);
        }

        // Below every row → append after the last item.
        var last = rows[^1];
        return (last.Index + 1, last.Bottom);
    }
}
