using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.ViewModels;
using Muselly.Web.Services;

namespace Muselly.Web.ViewModels;

/// <summary>
/// Sign-in form for a named account. The shell decides when to show this: it auto-enters as a guest when the
/// host allows guest access, and only falls back to this form when guests aren't allowed (or an admin chooses
/// to sign in for elevated access).
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly WebHostClient _client;
    private readonly WebSession _session;
    private readonly WebLibraryLoader _loader;

    public LoginViewModel(WebHostClient client, WebSession session, WebLibraryLoader loader)
    {
        _client = client;
        _session = session;
        _loader = loader;
    }

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _guestAllowed;
    [ObservableProperty] private string _serverName = "Muselly";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Fetches the host greeting (server name + whether guest browsing is allowed).</summary>
    public async Task LoadHelloAsync()
    {
        var hello = await _client.HelloAsync();
        if (hello is null)
        {
            Status = "Could not reach the server.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(hello.ServerName)) ServerName = hello.ServerName;
        GuestAllowed = hello.GuestEnabled;
    }

    /// <summary>Signs in as a guest and loads the library. Returns false if guest access is unavailable.</summary>
    public async Task<bool> TryGuestAsync()
    {
        var result = await _client.LoginAsync(string.Empty, string.Empty, guest: true);
        if (result is { Success: true })
        {
            _session.ServerName = ServerName;
            _session.SignIn(result.SessionToken, result.Role, "guest");
            await _loader.RefreshAsync();
            return true;
        }
        Status = result?.Error ?? "Guest access is unavailable.";
        return false;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Signing in…";
        try
        {
            var result = await _client.LoginAsync(Username, Password, guest: false);
            if (result is { Success: true })
            {
                _session.ServerName = ServerName;
                _session.SignIn(result.SessionToken, result.Role, Username);
                Password = string.Empty;
                Status = "Loading library…";
                await _loader.RefreshAsync();
            }
            else
            {
                Status = result?.Error ?? "Sign-in failed.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
