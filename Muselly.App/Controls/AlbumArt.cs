using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Muselly.App.Services;
using Muselly.Core.Util;

namespace Muselly.App.Controls;

/// <summary>
/// Displays album cover art with rounded corners and a themed music-note placeholder when artwork is
/// missing. Set <see cref="Source"/> to an artwork file path; the bitmap is resolved (and cached) by
/// <see cref="ArtworkCache"/>. Used everywhere art appears — grid cards, list rows, the now-playing bar
/// and the queue — so cover presentation stays consistent.
/// </summary>
public sealed class AlbumArt : TemplatedControl
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<AlbumArt, string?>(nameof(Source));

    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<AlbumArt, Bitmap?>(nameof(Bitmap));

    public static readonly StyledProperty<bool> HasImageProperty =
        AvaloniaProperty.Register<AlbumArt, bool>(nameof(HasImage));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The resolved bitmap (set internally from <see cref="Source"/>). Bound by the control template.</summary>
    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        private set => SetValue(BitmapProperty, value);
    }

    /// <summary>True when a bitmap is available (drives placeholder vs. image visibility in the template).</summary>
    public bool HasImage
    {
        get => GetValue(HasImageProperty);
        private set => SetValue(HasImageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            var source = Source;
            var bmp = ArtworkCache.Get(source);
            Bitmap = bmp;
            HasImage = bmp is not null;

            // Remote artwork is streamed in lazily; fetch then apply if this control still shows that source.
            if (bmp is null && RemoteSource.IsRemote(source))
                _ = LoadRemoteAsync(source!);
        }
    }

    private async System.Threading.Tasks.Task LoadRemoteAsync(string source)
    {
        var bmp = await ArtworkCache.GetRemoteAsync(source).ConfigureAwait(true);
        if (bmp is null) return;
        // The control is reused in virtualised lists — only apply if the source hasn't changed since.
        void Apply()
        {
            if (!string.Equals(Source, source, StringComparison.Ordinal)) return;
            Bitmap = bmp;
            HasImage = true;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }
}
