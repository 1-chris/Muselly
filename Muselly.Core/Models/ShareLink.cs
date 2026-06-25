namespace Muselly.Core.Models;

/// <summary>What a share link points at.</summary>
public enum ShareKind
{
    Album,
    Artist,
    Song,
    Playlist
}

/// <summary>
/// A temporary, revocable guest link to a single library item. Opening it grants a guest session scoped to
/// just that item (and its tracks) — the rest of the library stays hidden — even if global guest access is
/// off. Persisted to <c>shares.json</c>. The <see cref="Id"/> doubles as the opaque token in the URL.
/// </summary>
public sealed class ShareLink
{
    public required string Id { get; set; }

    public ShareKind Kind { get; set; }

    /// <summary>The target's key: album key, artist key, track id or playlist id.</summary>
    public required string Key { get; set; }

    /// <summary>A human label for management lists (e.g. the album/artist/song/playlist name).</summary>
    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>When the link stops working. Null means it never expires (until revoked).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt is { } e && DateTimeOffset.Now > e;
}

/// <summary>The access scope carried by a share-link session: it may only see this one item and its tracks.</summary>
public sealed class ShareScope
{
    public required ShareKind Kind { get; init; }
    public required string Key { get; init; }
}
