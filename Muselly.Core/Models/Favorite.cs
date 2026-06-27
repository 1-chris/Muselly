namespace Muselly.Core.Models;

/// <summary>What a <see cref="Favorite"/> points at.</summary>
public enum FavoriteKind
{
    Song = 0,
    Album = 1,
    Artist = 2
}

/// <summary>
/// A "favourited" library item. <see cref="Key"/> is the track id, album key or artist key depending on
/// <see cref="Kind"/>. <see cref="AddedAt"/> records when it was favourited (newest-first ordering).
/// </summary>
public sealed class Favorite
{
    public FavoriteKind Kind { get; set; }

    public required string Key { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
