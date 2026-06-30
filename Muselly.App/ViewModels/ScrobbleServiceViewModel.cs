using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>One scrobbling service row in Settings: shows connection state and lets the current user connect
/// (token for ListenBrainz; username + password for Last.fm / Libre.fm) or disconnect.</summary>
public sealed partial class ScrobbleServiceViewModel : ViewModelBase
{
    private readonly IScrobbleService _scrobble;
    private readonly Func<string> _currentUser;
    private bool _syncing;

    public ScrobbleServiceViewModel(ScrobbleProvider provider, string name, IScrobbleService scrobble, Func<string> currentUser)
    {
        Provider = provider;
        Name = name;
        _scrobble = scrobble;
        _currentUser = currentUser;
        IsAvailable = scrobble.IsProviderAvailable(provider);
        Refresh();
    }

    public ScrobbleProvider Provider { get; }
    public string Name { get; }
    public bool IsAvailable { get; }

    /// <summary>ListenBrainz authenticates with a single user token; the others use username + password.</summary>
    public bool UsesToken => Provider == ScrobbleProvider.ListenBrainz;
    public bool UsesPassword => !UsesToken;
    public string Field1Label => UsesToken ? "User token" : "Username";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _accountName;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _field1 = string.Empty;
    [ObservableProperty] private string _field2 = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public void Refresh()
    {
        _syncing = true;
        var account = (System.Collections.Generic.IReadOnlyList<ScrobbleAccount>)_scrobble.GetAccounts(_currentUser());
        ScrobbleAccount? mine = null;
        foreach (var a in account) if (a.Provider == Provider) { mine = a; break; }

        IsConnected = mine is not null;
        AccountName = mine?.AccountName;
        Enabled = mine?.Enabled ?? false;
        _syncing = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing || !IsConnected) return;
        _scrobble.SetEnabled(_currentUser(), Provider, value);
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Field1) || (UsesPassword && string.IsNullOrWhiteSpace(Field2)))
        {
            Status = UsesToken ? "Enter your user token." : "Enter your username and password.";
            return;
        }

        IsBusy = true;
        Status = "Connecting…";
        var result = await _scrobble.ConnectAsync(_currentUser(), Provider, Field1, UsesPassword ? Field2 : null);
        IsBusy = false;

        if (result.Success)
        {
            Field1 = string.Empty;
            Field2 = string.Empty;
            Status = $"Connected as {result.AccountName}.";
            Refresh();
        }
        else
        {
            Status = result.Error ?? "Couldn't connect.";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _scrobble.Disconnect(_currentUser(), Provider);
        Status = string.Empty;
        Refresh();
    }
}
