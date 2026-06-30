using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

namespace Muselly.App.Services;

/// <summary>
/// Loads and caches cover-art bitmaps by path. Local files are decoded once to a bounded width; remote
/// (<c>muselly://</c>) artwork is fetched from the owning server on demand via <see cref="RemoteManager"/>,
/// cached to disk and in memory, then decoded. Thread-safe; returns <c>null</c> for missing/unreadable art.
/// </summary>
public static class ArtworkCache
{
    private const int DecodeWidth = 320;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set by the platform head so remote artwork can be streamed in. Null on heads without networking.</summary>
    public static IRemoteServerManager? RemoteManager { get; set; }

    /// <summary>Synchronous lookup: returns a cached/local bitmap, or null (remote art needs <see cref="GetRemoteAsync"/>).</summary>
    public static Bitmap? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var cached))
                return cached;
        }

        if (RemoteSource.IsRemote(path))
            return null; // resolved asynchronously by GetRemoteAsync

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                bitmap = Bitmap.DecodeToWidth(stream, DecodeWidth);
            }
        }
        catch
        {
            bitmap = null;
        }

        lock (Gate)
        {
            Cache[path] = bitmap;
        }
        return bitmap;
    }

    /// <summary>
    /// Loads cover art at full (native) resolution, bypassing the thumbnail decode cap — for the cover
    /// lightbox, where we want maximum quality. Not cached (it's transient and can be large). Works for local
    /// files and remote (<c>muselly://</c>) art alike.
    /// </summary>
    public static async Task<Bitmap?> LoadFullAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (RemoteSource.IsRemote(path))
            {
                var manager = RemoteManager;
                if (manager is null) return null;
                var bytes = await manager.GetResourceAsync(path).ConfigureAwait(false);
                if (bytes is not { Length: > 0 }) return null;
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }

            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream); // full resolution (no DecodeToWidth)
            }
        }
        catch
        {
            // Unreadable/corrupt image — caller falls back to the thumbnail.
        }
        return null;
    }

    /// <summary>Fetches and decodes remote artwork (no-op for local paths, which use <see cref="Get"/>).</summary>
    public static async Task<Bitmap?> GetRemoteAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RemoteSource.IsRemote(path)) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var cached))
                return cached;
        }

        Bitmap? bitmap = null;
        var manager = RemoteManager;
        if (manager is not null)
        {
            try
            {
                var bytes = await manager.GetResourceAsync(path).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    bitmap = Bitmap.DecodeToWidth(ms, DecodeWidth);
                }
            }
            catch
            {
                bitmap = null;
            }
        }

        lock (Gate)
        {
            Cache[path] = bitmap;
        }
        return bitmap;
    }
}
