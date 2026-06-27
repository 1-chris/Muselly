using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.App.ViewModels;
using Muselly.Core.Models;
using Muselly.Web.Audio;
using Muselly.Web.Services;

namespace Muselly.Web.ViewModels;

/// <summary>
/// Root view model for the browser head. Handles the sign-in gate (auto-guest when allowed), URL routing
/// (the address bar reflects the current area and is restored on load / after sign-in), and opening guest
/// share links (which authenticate as a scoped guest and jump straight to the shared item).
/// </summary>
public sealed partial class WebShellViewModel : ViewModelBase
{
    private readonly WebSession _session;
    private readonly WebHostClient _client;
    private readonly WebLibraryLoader _loader;
    private readonly INavigationService _nav;

    private string? _pendingRoute;

    public WebShellViewModel(WebSession session, LoginViewModel login, MainViewModel main, AdminViewModel admin,
        WebHostClient client, WebLibraryLoader loader, INavigationService nav)
    {
        _session = session;
        Login = login;
        Main = main;
        Admin = admin;
        _client = client;
        _loader = loader;
        _nav = nav;

        _session.Changed += (_, _) => OnUi(OnSessionChanged);
        _nav.Changed += (_, _) => OnUi(SyncUrl);
    }

    public LoginViewModel Login { get; }
    public MainViewModel Main { get; }
    public AdminViewModel Admin { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectingVisible))]
    [NotifyPropertyChangedFor(nameof(LoginVisible))]
    [NotifyPropertyChangedFor(nameof(AppVisible))]
    private bool _isConnecting = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginVisible))]
    [NotifyPropertyChangedFor(nameof(AppVisible))]
    private bool _showLogin;

    [ObservableProperty] private bool _showAdmin;

    public bool IsAuthenticated => _session.IsAuthenticated;
    public bool IsAdmin => _session.IsAdmin;
    public bool CanUpgrade => _session.IsAuthenticated && _session.Role == UserRole.Guest;

    public bool ConnectingVisible => IsConnecting;
    public bool LoginVisible => !IsConnecting && ShowLogin;
    public bool AppVisible => !IsConnecting && IsAuthenticated && !ShowLogin;

    public async Task InitializeAsync()
    {
        IsConnecting = true;
        try
        {
            var path = Interop.GetPath();
            var shareToken = ParseShareToken(path);

            if (shareToken is not null && await TryOpenShareAsync(shareToken))
                return; // share opened a scoped guest session and navigated to the item

            _pendingRoute = ParseRoute(path);

            await Login.LoadHelloAsync();
            if (!_session.IsAuthenticated)
            {
                if (Login.GuestAllowed)
                {
                    var ok = await Login.TryGuestAsync();
                    ShowLogin = !ok;
                    if (ok) ApplyPendingRoute();
                }
                else
                {
                    ShowLogin = true;
                }
            }
            else
            {
                ApplyPendingRoute();
            }
        }
        catch
        {
            if (!_session.IsAuthenticated) ShowLogin = true;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task<bool> TryOpenShareAsync(string token)
    {
        var resp = await _client.ShareLoginAsync(token);
        if (resp is not { Success: true }) return false;

        _session.SignIn(resp.SessionToken, resp.Role, "guest");
        await _loader.RefreshAsync();
        _pendingRoute = ShareRoute(resp);
        ApplyPendingRoute();
        return true;
    }

    [RelayCommand] private void ShowSignIn() => ShowLogin = true;
    [RelayCommand] private void CancelSignIn() { if (_session.IsAuthenticated) ShowLogin = false; }
    [RelayCommand] private void ToggleAdmin() => ShowAdmin = !ShowAdmin;
    [RelayCommand] private void CloseAdmin() => ShowAdmin = false;

    private void OnSessionChanged()
    {
        if (_session.IsAuthenticated) ShowLogin = false;
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(LoginVisible));
        OnPropertyChanged(nameof(AppVisible));
        if (_session.IsAdmin) _ = Admin.LoadAsync();
        ApplyPendingRoute();
    }

    private void ApplyPendingRoute()
    {
        if (!_session.IsAuthenticated || string.IsNullOrEmpty(_pendingRoute)) return;
        var route = _pendingRoute;
        _pendingRoute = null;
        _nav.NavigateRoute(route);
    }

    private void SyncUrl()
    {
        if (_session.IsAuthenticated) Interop.SetPath("/" + _nav.CurrentRoute);
    }

    private static string ShareRoute(ShareLoginDto resp) => resp.Kind switch
    {
        ShareKind.Album => "album/" + Uri.EscapeDataString(resp.Key),
        ShareKind.Artist => "artist/" + Uri.EscapeDataString(resp.Key),
        _ => "songs" // a single song or a playlist surfaces as the (scoped) songs list
    };

    private static string ParseRoute(string path)
    {
        var q = path.IndexOf('?');
        var p = q >= 0 ? path[..q] : path;
        return p.Trim('/');
    }

    private static string? ParseShareToken(string path)
    {
        var q = path.IndexOf('?');
        if (q < 0) return null;
        foreach (var part in path[(q + 1)..].Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "share" && kv[1].Length > 0)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
