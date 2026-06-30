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

    private string? _pinned;
    private bool _attached;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            var source = Source;
            var bmp = ArtworkCache.Get(source);     // cached only — never decodes on the UI thread
            Bitmap = bmp;
            HasImage = bmp is not null;
            UpdatePin();

            if (bmp is null && !string.IsNullOrWhiteSpace(source))
            {
                // Decode/fetch off the UI thread, then apply if this control still shows that source. Keeps
                // scrolling smooth (no synchronous image decode while a page of cards is created).
                if (RemoteSource.IsRemote(source))
                    _ = ApplyAsync(source!, ArtworkCache.GetRemoteAsync(source!));
                else
                    _ = ApplyAsync(source!, ArtworkCache.GetLocalAsync(source));
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdatePin();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdatePin();
    }

    /// <summary>Pins the cover the control is currently showing (so the cache won't dispose it while it's on
    /// screen), and releases any previous pin.</summary>
    private void UpdatePin()
    {
        // Only protect art we're actually displaying while attached to the tree.
        var desired = _attached && HasImage ? Source : null;
        if (string.Equals(desired, _pinned, StringComparison.Ordinal)) return;
        ArtworkCache.Unpin(_pinned);
        _pinned = desired;
        ArtworkCache.Pin(_pinned);
    }

    private async System.Threading.Tasks.Task ApplyAsync(string source, System.Threading.Tasks.Task<Bitmap?> load)
    {
        var bmp = await load.ConfigureAwait(true);
        if (bmp is null) return;
        // The control is reused in virtualised lists — only apply if the source hasn't changed since.
        void Apply()
        {
            if (!string.Equals(Source, source, StringComparison.Ordinal)) return;
            Bitmap = bmp;
            HasImage = true;
            UpdatePin();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }
}
