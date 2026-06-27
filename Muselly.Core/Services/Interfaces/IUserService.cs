using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Owns user identity, profiles and privacy. There is always a built-in local user (full admin, no local
/// login). Remote/web accounts (from <see cref="IServerUserStore"/>) surface here too, each with a profile.
/// Privacy and edit permissions are enforced centrally: a user may only edit their own profile, except admins
/// who may edit anyone; remote users may not change their username (only the built-in user can rename itself).
/// </summary>
public interface IUserService
{
    /// <summary>The acting user for this process (the built-in local user on the desktop).</summary>
    UserProfile Current { get; }

    bool IsCurrentAdmin { get; }

    /// <summary>The built-in local user can log in remotely only once it has a username and password set.</summary>
    bool RemoteLoginEnabled { get; }

    event EventHandler? Changed;

    /// <summary>Every known user: the built-in user plus any server accounts, each with its profile.</summary>
    IReadOnlyList<UserProfile> AllUsers { get; }

    UserProfile? Get(string username);

    /// <summary>The users <paramref name="viewer"/> may see in the directory: admins see everyone; others see
    /// only users whose profile visibility is public (always including the viewer themselves).</summary>
    IReadOnlyList<UserProfile> VisibleTo(UserProfile viewer);

    /// <summary>True if <paramref name="viewer"/> may edit <paramref name="targetUsername"/> (self, or admin).</summary>
    bool CanEdit(UserProfile viewer, string targetUsername);

    /// <summary>Whether <paramref name="viewer"/> may see the given facet of <paramref name="target"/>
    /// (own data and admins: always; otherwise only when that facet is public).</summary>
    bool CanView(UserProfile viewer, UserProfile target, ProfileFacet facet);

    /// <summary>Applies profile/privacy edits on behalf of <paramref name="actingUsername"/>, enforcing
    /// permissions. The username field is honoured only for the built-in user. Returns false if not permitted.</summary>
    bool UpdateProfile(string actingUsername, UserProfile updated);

    Task LoadAsync();
}

/// <summary>A privacy-controlled facet of a user profile.</summary>
public enum ProfileFacet
{
    Profile,
    Favorites,
    NowPlaying,
    ListeningHistory
}
