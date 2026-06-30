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

    // Bounded LRU: scrolling a huge library would otherwise decode tens of thousands of bitmaps and keep
    // them forever (multi-GB). We cap the live bitmaps and evict the least-recently-used; combined with a
    // virtualizing grid (which releases off-screen card references), memory stays flat.
    private const int MaxEntries = 384;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> Map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<Entry> Lru = new(); // most-recent at the front

    // Paths currently displayed by a live AlbumArt control (ref-counted). Pinned entries are never disposed
    // on eviction, so we can safely dispose everything else promptly to free native (Skia) memory.
    private static readonly Dictionary<string, int> Pins = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public required string Key;
        public Bitmap? Bitmap;
    }

    /// <summary>Set by the platform head so remote artwork can be streamed in. Null on heads without networking.</summary>
    public static IRemoteServerManager? RemoteManager { get; set; }

    /// <summary>Synchronous lookup: returns an already-cached bitmap, or null. A null result for a local path
    /// means "not decoded yet" — call <see cref="GetLocalAsync"/> to decode it off the UI thread.</summary>
    public static Bitmap? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Gate)
        {
            if (!Map.TryGetValue(path, out var node)) return null;
            Touch(node); // mark most-recently-used
            return node.Value.Bitmap;
        }
    }

    /// <summary>Inserts/refreshes a cache entry and evicts the least-recently-used beyond the cap. Caller must
    /// not hold a disposed bitmap: evicted bitmaps are disposed, and the cap is far larger than any viewport,
    /// so an evicted entry is one that scrolled out of view long ago.</summary>
    private static void Store(string path, Bitmap? bitmap)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(path, out var existing))
            {
                existing.Value.Bitmap = bitmap;
                Touch(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry { Key = path, Bitmap = bitmap });
            Lru.AddFirst(node);
            Map[path] = node;
            EvictIfNeeded();
        }
    }

    /// <summary>Evicts least-recently-used entries beyond the cap, disposing each evicted bitmap to free its
    /// native memory immediately. Pinned entries (currently on screen) are skipped so a displayed cover is
    /// never disposed out from under a control.</summary>
    private static void EvictIfNeeded()
    {
        while (Map.Count > MaxEntries)
        {
            // Find the least-recently-used entry that isn't pinned.
            var node = Lru.Last;
            while (node is not null && Pins.ContainsKey(node.Value.Key))
                node = node.Previous;
            if (node is null) break; // everything live is pinned (cap is far larger than the viewport)

            Map.Remove(node.Value.Key);
            Lru.Remove(node);
            node.Value.Bitmap?.Dispose();
        }
    }

    /// <summary>Marks a path as on-screen so its cached bitmap won't be evicted/disposed. Ref-counted.</summary>
    public static void Pin(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate)
            Pins[path] = Pins.TryGetValue(path, out var n) ? n + 1 : 1;
    }

    /// <summary>Releases a pin taken by <see cref="Pin"/>.</summary>
    public static void Unpin(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate)
        {
            if (!Pins.TryGetValue(path, out var n)) return;
            if (n <= 1) Pins.Remove(path);
            else Pins[path] = n - 1;
        }
    }

    private static void Touch(LinkedListNode<Entry> node)
    {
        if (Lru.First == node) return;
        Lru.Remove(node);
        Lru.AddFirst(node);
    }

    /// <summary>Returns true if the path is cached (even as a null "no art" result), so the async loaders can
    /// skip re-decoding/re-fetching known-missing art.</summary>
    private static bool TryGetCached(string path, out Bitmap? bitmap)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(path, out var node))
            {
                Touch(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
            bitmap = null;
            return false;
        }
    }

    /// <summary>
    /// Decodes a local cover off the UI thread (bounded to the thumbnail width) and caches it, so populating
    /// a page of cards never blocks the UI. Returns the cached bitmap immediately if already decoded.
    /// </summary>
    public static async Task<Bitmap?> GetLocalAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || RemoteSource.IsRemote(path)) return null;

        if (TryGetCached(path, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                if (!File.Exists(path)) return (Bitmap?)null;
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, DecodeWidth);
            }).ConfigureAwait(true);
        }
        catch
        {
            bitmap = null;
        }

        Store(path, bitmap);
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
                // Decode off the UI thread so opening the lightbox never hitches.
                return await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return new Bitmap(stream); // full resolution (no DecodeToWidth)
                }).ConfigureAwait(true);
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

        if (TryGetCached(path, out var cached)) return cached;

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

        Store(path, bitmap);
        return bitmap;
    }
}
