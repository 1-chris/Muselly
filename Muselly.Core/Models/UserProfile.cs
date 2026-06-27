namespace Muselly.Core.Models;

/// <summary>Whether a piece of a user's profile is visible to others. Everything defaults to private.</summary>
public enum PrivacyVisibility
{
    Private = 0,
    Public = 1
}

/// <summary>
/// A user's public-facing profile and privacy preferences, kept separately from the authentication record
/// (<see cref="ServerUser"/>). The desktop has exactly one <see cref="IsBuiltIn"/> profile — the local
/// machine user, who is always an admin and is never asked to log in locally. Remote/web users get a profile
/// the first time they appear. Serialised to <c>user-profiles.json</c>.
/// </summary>
public sealed class UserProfile
{
    public required string Username { get; set; }

    /// <summary>True for the single local machine user that owns this install (full admin, no local login).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Optional short self-description shown on the user page.</summary>
    public string? Bio { get; set; }

    /// <summary>Absolute path to a chosen profile picture, if any.</summary>
    public string? ProfilePicturePath { get; set; }

    public PrivacyVisibility FavoritesVisibility { get; set; } = PrivacyVisibility.Private;

    public PrivacyVisibility NowPlayingVisibility { get; set; } = PrivacyVisibility.Private;

    public PrivacyVisibility ListeningHistoryVisibility { get; set; } = PrivacyVisibility.Private;

    /// <summary>Whether the profile itself is discoverable/visible in the Users directory.</summary>
    public PrivacyVisibility ProfileVisibility { get; set; } = PrivacyVisibility.Private;

    /// <summary>For the built-in user: true once a username AND password have been set, which is what enables
    /// logging in from the web/server interfaces. Until then, remote login as this user is disabled.</summary>
    public bool RemoteLoginEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public UserProfile Clone() => new()
    {
        Username = Username,
        IsBuiltIn = IsBuiltIn,
        Bio = Bio,
        ProfilePicturePath = ProfilePicturePath,
        FavoritesVisibility = FavoritesVisibility,
        NowPlayingVisibility = NowPlayingVisibility,
        ListeningHistoryVisibility = ListeningHistoryVisibility,
        ProfileVisibility = ProfileVisibility,
        RemoteLoginEnabled = RemoteLoginEnabled,
        CreatedAt = CreatedAt
    };
}
