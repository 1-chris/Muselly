using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Muselly.App.Services;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class AlbumDetailView : UserControl
{
    public AlbumDetailView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Opens the album cover as a window-filling lightbox with a darkened backdrop and drop shadow.</summary>
    private void OnArtClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not AlbumDetailViewModel vm) return;
        var source = vm.Album.ArtworkPath;
        if (string.IsNullOrEmpty(source)) return;

        var layer = OverlayLayer.GetOverlayLayer(this);
        if (layer is null) return;

        var image = new Image
        {
            Stretch = Stretch.Uniform,           // fill the window, upscaling even low-res covers
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(48),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 48, Opacity = 0.7, OffsetX = 0, OffsetY = 12 }
        };

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDA, 0, 0, 0)),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = image
        };

        // OverlayLayer is canvas-like, so size the backdrop to the layer's bounds (and track resizes).
        void SizeToLayer()
        {
            backdrop.Width = layer.Bounds.Width;
            backdrop.Height = layer.Bounds.Height;
        }
        SizeToLayer();
        void OnLayerChanged(object? s, AvaloniaPropertyChangedEventArgs ev)
        {
            if (ev.Property == BoundsProperty) SizeToLayer();
        }
        layer.PropertyChanged += OnLayerChanged;

        void Close()
        {
            layer.PropertyChanged -= OnLayerChanged;
            layer.Children.Remove(backdrop);
        }
        backdrop.PointerPressed += (_, _) => Close();

        // Show the cached thumbnail instantly (no flash), then swap in the full-resolution image so the
        // enlarged cover is as crisp as the source allows.
        var thumb = ArtworkCache.Get(source);
        if (thumb is not null) image.Source = thumb;
        _ = LoadFullAsync(source, image);

        layer.Children.Add(backdrop);
    }

    private static async System.Threading.Tasks.Task LoadFullAsync(string source, Image target)
    {
        var full = await ArtworkCache.LoadFullAsync(source).ConfigureAwait(true);
        if (full is null) return;
        void Apply() => target.Source = full;
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    private void OnTrackMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AlbumDetailViewModel vm) return;
        if (sender is not MenuItem item) return;
        var data = item.DataContext;
        switch (item.Tag as string)
        {
            case "track.play": vm.PlayTrackCommand.Execute(data); break;
            case "track.playnext": vm.PlayTrackNextCommand.Execute(data); break;
            case "track.queue": vm.AddTrackToQueueCommand.Execute(data); break;
        }
    }
}
