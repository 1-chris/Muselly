using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Web.Services;

/// <summary>
/// Browser <see cref="IUserService"/> backed by the host's user API. The acting user is whoever signed in;
/// the users directory, profiles and per-facet visibility/permissions are computed by the host (the client
/// just caches the answers). Profile edits are sent to the host. (Re)loads on sign-in.
/// </summary>
public sealed class WebUserService : IUserService
{
    private readonly WebHostClient _client;
    private readonly WebSession _session;
    private readonly object _gate = new();

    private readonly Dictionary<string, UserProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Perms> _perms = new(StringComparer.OrdinalIgnoreCase);
    private List<UserProfile> _all = new();
    private bool _currentIsAdmin;

    public WebUserService(WebHostClient client, WebSession session)
    {
        _client = client;
        _session = session;
        _session.Changed += (_, _) => _ = LoadAsync();
    }

    public UserProfile Current
    {
        get { lock (_gate) return _profiles.GetValueOrDefault(_session.Username) ?? new UserProfile { Username = _session.Username }; }
    }

    public bool IsCurrentAdmin { get { lock (_gate) return _currentIsAdmin; } }

    public bool RemoteLoginEnabled => true; // remote login is, by definition, how this client connected

    public event EventHandler? Changed;

    public IReadOnlyList<UserProfile> AllUsers { get { lock (_gate) return _all.ToArray(); } }

    public UserProfile? Get(string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        lock (_gate) return _profiles.GetValueOrDefault(username);
    }

    // The host already filtered the list to what this user may see.
    public IReadOnlyList<UserProfile> VisibleTo(UserProfile viewer) => AllUsers;

    public bool CanEdit(UserProfile viewer, string targetUsername)
    {
        lock (_gate) return _perms.TryGetValue(targetUsername, out var p) && p.CanEdit;
    }

    public bool CanView(UserProfile viewer, UserProfile target, ProfileFacet facet)
    {
        lock (_gate)
        {
            if (!_perms.TryGetValue(target.Username, out var p)) return false;
            return facet switch
            {
                ProfileFacet.Favorites => p.CanViewFavorites,
                ProfileFacet.NowPlaying => p.CanViewNowPlaying,
                ProfileFacet.ListeningHistory => p.CanViewHistory,
                _ => true // the profile is in the visible list, so the profile facet itself is viewable
            };
        }
    }

    public bool UpdateProfile(string actingUsername, UserProfile updated)
    {
        _ = SubmitAsync(updated);
        return true; // optimistic; a reload follows the host write
    }

    public Task LoadAsync() => RefreshAsync();

    private async Task SubmitAsync(UserProfile updated)
    {
        await _client.UpdateProfileAsync(new
        {
            username = updated.Username,
            bio = updated.Bio,
            profileVisibility = updated.ProfileVisibility,
            favoritesVisibility = updated.FavoritesVisibility,
            nowPlayingVisibility = updated.NowPlayingVisibility,
            listeningHistoryVisibility = updated.ListeningHistoryVisibility
        }).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        if (!_session.IsAuthenticated || _session.Role == UserRole.Guest)
        {
            lock (_gate) { _profiles.Clear(); _perms.Clear(); _all = new(); _currentIsAdmin = false; }
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var list = await _client.GetUsersAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _profiles.Clear();
            _perms.Clear();
            _all = new();
            foreach (var dto in list?.Users ?? new())
            {
                var profile = ToProfile(dto);
                _profiles[profile.Username] = profile;
                _perms[profile.Username] = new Perms(dto.CanEdit, dto.CanViewFavorites, dto.CanViewNowPlaying, dto.CanViewHistory);
                _all.Add(profile);
                if (string.Equals(profile.Username, _session.Username, StringComparison.OrdinalIgnoreCase))
                    _currentIsAdmin = dto.IsAdmin;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static UserProfile ToProfile(UserProfileClientDto dto) => new()
    {
        Username = dto.Username,
        IsBuiltIn = dto.IsBuiltIn,
        Bio = dto.Bio,
        ProfileVisibility = dto.ProfileVisibility,
        FavoritesVisibility = dto.FavoritesVisibility,
        NowPlayingVisibility = dto.NowPlayingVisibility,
        ListeningHistoryVisibility = dto.ListeningHistoryVisibility,
        RemoteLoginEnabled = dto.RemoteLoginEnabled
    };

    private readonly record struct Perms(bool CanEdit, bool CanViewFavorites, bool CanViewNowPlaying, bool CanViewHistory);
}
