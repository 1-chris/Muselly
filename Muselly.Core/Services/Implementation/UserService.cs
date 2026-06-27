using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IUserService"/>. Maintains the built-in local user (always admin; no local login) and a
/// profile per known user, persisted to <c>user-profiles.json</c>. Server accounts (from the optional
/// <see cref="IServerUserStore"/>) are surfaced alongside, each gaining a private-by-default profile on first
/// sight. The acting user is the built-in user (the desktop has direct-machine trust).
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IServerUserStore? _serverUsers;
    private readonly object _gate = new();
    private readonly Dictionary<string, UserProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private UserProfile _builtIn;

    public UserService(IServerUserStore? serverUsers = null)
    {
        _serverUsers = serverUsers;
        _builtIn = CreateDefaultBuiltIn();
        _profiles[_builtIn.Username] = _builtIn;
    }

    public UserProfile Current => _builtIn;
    public bool IsCurrentAdmin => true; // the built-in local user is always an admin
    public bool RemoteLoginEnabled => _builtIn.RemoteLoginEnabled;

    public event EventHandler? Changed;

    public IReadOnlyList<UserProfile> AllUsers
    {
        get
        {
            lock (_gate)
            {
                var list = new List<UserProfile> { _builtIn };
                if (_serverUsers is not null)
                {
                    foreach (var su in _serverUsers.Users)
                    {
                        if (string.Equals(su.Username, _builtIn.Username, StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(GetOrCreateLocked(su.Username));
                    }
                }
                return list;
            }
        }
    }

    public UserProfile? Get(string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        lock (_gate) return _profiles.GetValueOrDefault(username);
    }

    public IReadOnlyList<UserProfile> VisibleTo(UserProfile viewer)
    {
        var all = AllUsers;
        if (IsAdmin(viewer)) return all;

        var visible = new List<UserProfile>();
        foreach (var u in all)
            if (u.ProfileVisibility == PrivacyVisibility.Public ||
                string.Equals(u.Username, viewer.Username, StringComparison.OrdinalIgnoreCase))
                visible.Add(u);
        return visible;
    }

    public bool CanEdit(UserProfile viewer, string targetUsername) =>
        IsAdmin(viewer) || string.Equals(viewer.Username, targetUsername, StringComparison.OrdinalIgnoreCase);

    public bool CanView(UserProfile viewer, UserProfile target, ProfileFacet facet)
    {
        if (IsAdmin(viewer) || string.Equals(viewer.Username, target.Username, StringComparison.OrdinalIgnoreCase))
            return true;

        var visibility = facet switch
        {
            ProfileFacet.Favorites => target.FavoritesVisibility,
            ProfileFacet.NowPlaying => target.NowPlayingVisibility,
            ProfileFacet.ListeningHistory => target.ListeningHistoryVisibility,
            _ => target.ProfileVisibility
        };
        return visibility == PrivacyVisibility.Public;
    }

    public bool UpdateProfile(string actingUsername, UserProfile updated)
    {
        var acting = Get(actingUsername) ?? _builtIn;

        lock (_gate)
        {
            // Resolve the profile being edited. The built-in user is identified by its flag — never by
            // username — so a rename updates it in place instead of spawning a new profile under the new
            // name. Everyone else is keyed by username (and can't change it).
            var target = updated.IsBuiltIn ? _builtIn : GetOrCreateLocked(updated.Username);

            if (!CanEdit(acting, target.Username)) return false;

            if (target.IsBuiltIn)
            {
                var newName = updated.Username?.Trim();
                if (!string.IsNullOrWhiteSpace(newName) &&
                    !string.Equals(newName, target.Username, StringComparison.Ordinal))
                {
                    _profiles.Remove(target.Username);
                    target.Username = newName!;
                    _profiles[target.Username] = target;
                }
                target.RemoteLoginEnabled = updated.RemoteLoginEnabled;
            }

            target.Bio = updated.Bio;
            target.ProfilePicturePath = updated.ProfilePicturePath;
            target.FavoritesVisibility = updated.FavoritesVisibility;
            target.NowPlayingVisibility = updated.NowPlayingVisibility;
            target.ListeningHistoryVisibility = updated.ListeningHistoryVisibility;
            target.ProfileVisibility = updated.ProfileVisibility;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task LoadAsync() => Task.Run(() =>
    {
        var snapshot = JsonStore.Load(StoragePaths.UserProfilesFile(), () => new ProfilesSnapshot());
        lock (_gate)
        {
            _profiles.Clear();
            foreach (var p in snapshot.Profiles)
                if (!string.IsNullOrEmpty(p.Username))
                    _profiles[p.Username] = p;

            _builtIn = FindBuiltInLocked() ?? CreateDefaultBuiltIn();
            _profiles[_builtIn.Username] = _builtIn;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    });

    private bool IsAdmin(UserProfile profile) =>
        profile.IsBuiltIn || _serverUsers?.Find(profile.Username)?.Role == UserRole.Admin;

    private UserProfile GetOrCreateLocked(string username)
    {
        if (_profiles.TryGetValue(username, out var existing)) return existing;
        var profile = new UserProfile { Username = username };
        _profiles[username] = profile;
        return profile;
    }

    private UserProfile? FindBuiltInLocked()
    {
        foreach (var p in _profiles.Values)
            if (p.IsBuiltIn) return p;
        return null;
    }

    private static UserProfile CreateDefaultBuiltIn()
    {
        var name = Environment.UserName;
        if (string.IsNullOrWhiteSpace(name)) name = "Me";
        return new UserProfile { Username = name, IsBuiltIn = true };
    }

    private void Save()
    {
        ProfilesSnapshot snapshot;
        lock (_gate) snapshot = new ProfilesSnapshot { Profiles = new List<UserProfile>(_profiles.Values) };
        _ = Task.Run(() =>
        {
            try { JsonStore.Save(StoragePaths.UserProfilesFile(), snapshot); }
            catch { /* best-effort */ }
        });
    }

    private sealed class ProfilesSnapshot
    {
        public int Version { get; set; } = 1;
        public List<UserProfile> Profiles { get; set; } = new();
    }
}
