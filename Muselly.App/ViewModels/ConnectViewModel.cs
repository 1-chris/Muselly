using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;
using WebHttp = Muselly.Core.Services.Web.WebClient;

namespace Muselly.App.ViewModels;

/// <summary>
/// The "Connect" panel: hosts/configures the built-in server (bitrate, cache, guest access, UPnP, users) and
/// manages connections to other Muselly servers (add, connect, verify fingerprint, disconnect, forget).
/// </summary>
public sealed partial class ConnectViewModel : ViewModelBase
{
    private readonly IServerHost _host;
    private readonly IRemoteServerManager _remotes;
    private readonly IWebServerHost _webHost;
    private readonly Muselly.App.Services.IClientContext _clientContext;
    private readonly IShareService _shares;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlists;

    public ConnectViewModel(IServerHost host, IRemoteServerManager remotes, IWebServerHost webHost,
        Muselly.App.Services.IClientContext clientContext, IShareService shares, ILibraryService library,
        IPlaylistService playlists)
    {
        _host = host;
        _remotes = remotes;
        _webHost = webHost;
        _clientContext = clientContext;
        _shares = shares;
        _library = library;
        _playlists = playlists;

        _host.StateChanged += (_, _) => OnUi(RefreshServerState);
        _host.Logged += (_, entry) => OnUi(() => AppendLog(entry));
        _remotes.ServersChanged += (_, _) => OnUi(RefreshRemotes);
        _webHost.StateChanged += (_, _) => OnUi(RefreshWebState);

        foreach (var entry in _host.RecentLogs) AppendLog(entry);

        var s = _host.Settings;
        _webEnabled = s.WebEnabled;
        _webHttpPort = s.WebHttpPort;
        _webHttpsPort = s.WebHttpsPort;
        _webUpnpEnabled = s.WebUpnpEnabled;
        _webExternalUrl = s.WebExternalUrl;
        _serverEnabled = s.Enabled;
        _serverName = s.ServerName;
        _selectedBitrate = s.OpusBitrateKbps is >= 64 and <= 256 ? s.OpusBitrateKbps : 128;
        _transcodeCacheGb = Math.Round(s.TranscodeCacheMaxBytes / (double)(1024 * 1024 * 1024), 1);
        _guestEnabled = s.GuestEnabled;
        _upnpEnabled = s.UpnpEnabled;
        _newUserRole = UserRole.User;
        _addPort = 0;

        RefreshServerState();
        RefreshUsers();
        RefreshRemotes();
        RefreshFirewall();
        RefreshWebState();
        ReloadShareTargets();
        RefreshShares();
    }

    /// <summary>True when the host's server/web/firewall/user settings can be managed from this client.</summary>
    public bool CanManageHost => _clientContext.CanManageHost;

    // --- Built-in server -----------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowServerLogs))]
    private bool _serverEnabled;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Server off";

    /// <summary>The activity log is relevant whenever either the TLS server or the web server is enabled.</summary>
    public bool ShowServerLogs => ServerEnabled || WebEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private int _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private string _fingerprint = string.Empty;

    [ObservableProperty] private int _connectedClients;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private string _serverName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private string _externalIp = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    private string _localIp = string.Empty;

    [ObservableProperty] private string _copyStatus = string.Empty;

    /// <summary>Human-friendly block of connection details to hand to someone who wants to connect.</summary>
    public string ShareText
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(ServerName) ? "Muselly Server" : ServerName;
            var host = !string.IsNullOrEmpty(ExternalIp) ? ExternalIp
                : !string.IsNullOrEmpty(LocalIp) ? LocalIp : "<your-ip>";
            var sb = new StringBuilder();
            sb.AppendLine($"Muselly server: {name}");
            sb.AppendLine($"Host: {host}");
            if (!string.IsNullOrEmpty(ExternalIp) && !string.IsNullOrEmpty(LocalIp) && ExternalIp != LocalIp)
                sb.AppendLine($"On the same network use: {LocalIp}");
            sb.AppendLine($"Port: {Port}");
            sb.AppendLine($"Fingerprint: {Fingerprint}");
            return sb.ToString().TrimEnd();
        }
    }
    [ObservableProperty] private int _selectedBitrate;
    [ObservableProperty] private double _transcodeCacheGb;
    [ObservableProperty] private bool _guestEnabled;
    [ObservableProperty] private bool _upnpEnabled;
    [ObservableProperty] private string _settingsStatus = string.Empty;

    public int[] BitrateOptions { get; } = { 64, 96, 128, 192, 256 };

    public ObservableCollection<ServerUserRow> Users { get; } = new();

    [ObservableProperty] private string _newUsername = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private UserRole _newUserRole;

    public UserRole[] RoleOptions { get; } = { UserRole.User, UserRole.Admin };

    private bool _suppressEnableHandler;

    async partial void OnServerEnabledChanged(bool value)
    {
        if (_suppressEnableHandler) return;
        try
        {
            await _host.UpdateSettingsAsync(s => s.Enabled = value);
            if (value && !_host.IsRunning) await _host.StartAsync();
            else if (!value && _host.IsRunning) await _host.StopAsync();
        }
        catch (Exception ex)
        {
            SettingsStatus = $"Could not {(value ? "start" : "stop")} server: {ex.Message}";
        }
        RefreshServerState();
    }

    [RelayCommand]
    private async Task SaveServerSettings()
    {
        try
        {
            await _host.UpdateSettingsAsync(s =>
            {
                s.ServerName = ServerName;
                s.OpusBitrateKbps = SelectedBitrate;
                s.TranscodeCacheMaxBytes = (long)Math.Max(0.1, TranscodeCacheGb) * 1024 * 1024 * 1024;
                s.GuestEnabled = GuestEnabled;
                s.UpnpEnabled = UpnpEnabled;
            });
            SettingsStatus = "Settings saved.";
        }
        catch (Exception ex)
        {
            SettingsStatus = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddUser()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrEmpty(NewPassword))
        {
            SettingsStatus = "Enter a username and password to add a user.";
            return;
        }
        _host.Users.AddOrUpdate(NewUsername.Trim(), NewPassword, NewUserRole);
        NewUsername = string.Empty;
        NewPassword = string.Empty;
        RefreshUsers();
    }

    [RelayCommand]
    private void RemoveUser(ServerUserRow? row)
    {
        if (row is null) return;
        _host.Users.Remove(row.Username);
        RefreshUsers();
    }

    [RelayCommand]
    private void PromoteUser(ServerUserRow? row)
    {
        if (row is null) return;
        var next = row.Role == UserRole.Admin ? UserRole.User : UserRole.Admin;
        _host.Users.SetRole(row.Username, next);
        RefreshUsers();
    }

    private bool _networkInfoStarted;

    private void RefreshServerState()
    {
        IsRunning = _host.IsRunning;
        Port = _host.Port;
        Fingerprint = _host.Fingerprint;
        ConnectedClients = _host.ConnectedClients;
        ServerEnabledSilently(_host.Settings.Enabled);
        StatusText = IsRunning
            ? $"Running on port {Port} — {ConnectedClients} connected"
            : (ServerEnabled ? "Starting…" : "Server off");

        if (IsRunning) EnsureNetworkInfo();
        else { _networkInfoStarted = false; CopyStatus = string.Empty; }
    }

    /// <summary>Detects the local IP (instant) and external IP (one-time web lookup) once the server is up.</summary>
    private void EnsureNetworkInfo()
    {
        if (_networkInfoStarted) return;
        _networkInfoStarted = true;
        LocalIp = DetectLocalIp();
        _ = RefreshExternalIpCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RefreshExternalIp()
    {
        ExternalIp = "Detecting…";
        try
        {
            var ip = await WebHttp.GetStringAsync("https://api.ipify.org");
            ExternalIp = string.IsNullOrWhiteSpace(ip) ? string.Empty : ip.Trim();
        }
        catch
        {
            ExternalIp = string.Empty;
        }
    }

    /// <summary>Records that the share details were copied (the view performs the actual clipboard write).</summary>
    public void MarkDetailsCopied() => CopyStatus = "Copied to clipboard.";

    // --- Server activity log -------------------------------------------------------------------------

    private const int MaxLogRows = 300;

    /// <summary>The server's recent activity, newest first.</summary>
    public ObservableCollection<ServerLogEntry> ServerLogs { get; } = new();

    [ObservableProperty] private bool _hasServerLogs;

    private void AppendLog(ServerLogEntry entry)
    {
        ServerLogs.Insert(0, entry);
        while (ServerLogs.Count > MaxLogRows) ServerLogs.RemoveAt(ServerLogs.Count - 1);
        HasServerLogs = ServerLogs.Count > 0;
    }

    [RelayCommand]
    private void ClearServerLogs()
    {
        ServerLogs.Clear();
        HasServerLogs = false;
    }

    private static string DetectLocalIp()
    {
        try
        {
            // Connecting a UDP socket doesn't send anything but selects the outbound interface's address.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ServerEnabledSilently(bool value)
    {
        // Reflect the real running state on the bound toggle without re-triggering the start/stop handler.
        if (ServerEnabled == value) return;
        _suppressEnableHandler = true;
        ServerEnabled = value;
        _suppressEnableHandler = false;
    }

    private void RefreshUsers()
    {
        Users.Clear();
        foreach (var u in _host.Users.Users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
            Users.Add(new ServerUserRow(u.Username, u.Role));
    }

    // --- Web server ----------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowServerLogs))]
    private bool _webEnabled;
    [ObservableProperty] private bool _webRunning;
    [ObservableProperty] private int _webHttpPort;
    [ObservableProperty] private int _webHttpsPort;
    [ObservableProperty] private bool _webUpnpEnabled;
    [ObservableProperty] private string _webExternalUrl = string.Empty;
    [ObservableProperty] private string _webStatusText = "Web server off";

    private bool _suppressWebEnableHandler;

    async partial void OnWebEnabledChanged(bool value)
    {
        if (_suppressWebEnableHandler) return;
        try
        {
            await _host.UpdateSettingsAsync(s => s.WebEnabled = value);
            if (value && !_webHost.IsRunning) await _webHost.StartAsync();
            else if (!value && _webHost.IsRunning) await _webHost.StopAsync();
        }
        catch (Exception ex)
        {
            WebStatusText = $"Could not {(value ? "start" : "stop")} web server: {ex.Message}";
        }
        RefreshWebState();
    }

    private bool _suppressWebUpnpHandler;

    async partial void OnWebUpnpEnabledChanged(bool value)
    {
        if (_suppressWebUpnpHandler) return;
        await _host.UpdateSettingsAsync(s => s.WebUpnpEnabled = value);
        await RestartWebIfRunning();
        RefreshWebState();
    }

    [RelayCommand]
    private async Task SaveWebSettings()
    {
        await _host.UpdateSettingsAsync(s =>
        {
            s.WebHttpPort = WebHttpPort;
            s.WebHttpsPort = WebHttpsPort;
            s.WebExternalUrl = (WebExternalUrl ?? string.Empty).Trim();
        });
        await RestartWebIfRunning();
        RefreshShares(); // share URLs may now use the external prefix
        RefreshWebState();
    }

    /// <summary>Applies web settings changes (ports / UPnP) by cycling the server when it's running.</summary>
    private async Task RestartWebIfRunning()
    {
        if (!_webHost.IsRunning) return;
        await _webHost.StopAsync();
        try { await _webHost.StartAsync(); } catch { /* surfaced via state */ }
    }

    private void RefreshWebState()
    {
        WebRunning = _webHost.IsRunning;
        _suppressWebEnableHandler = true;
        if (WebEnabled != _host.Settings.WebEnabled) WebEnabled = _host.Settings.WebEnabled;
        _suppressWebEnableHandler = false;

        _suppressWebUpnpHandler = true;
        if (WebUpnpEnabled != _host.Settings.WebUpnpEnabled) WebUpnpEnabled = _host.Settings.WebUpnpEnabled;
        _suppressWebUpnpHandler = false;

        if (_webHost.IsRunning)
        {
            if (string.IsNullOrEmpty(LocalIp)) LocalIp = DetectLocalIp();
            var parts = new System.Collections.Generic.List<string>();
            if (_webHost.HttpPort > 0) parts.Add($"http://{(string.IsNullOrEmpty(LocalIp) ? "localhost" : LocalIp)}:{_webHost.HttpPort}");
            if (_webHost.HttpsPort > 0) parts.Add($"https://{(string.IsNullOrEmpty(LocalIp) ? "localhost" : LocalIp)}:{_webHost.HttpsPort}");
            WebStatusText = "Serving at " + string.Join("  •  ", parts);
        }
        else
        {
            WebStatusText = _webHost.LastError is { Length: > 0 } err ? $"Web server error: {err}" : "Web server off";
        }
    }

    // --- Share links ---------------------------------------------------------------------------------

    public ShareKind[] ShareKindOptions { get; } = { ShareKind.Album, ShareKind.Artist, ShareKind.Song, ShareKind.Playlist };

    [ObservableProperty] private ShareKind _selectedShareKind = ShareKind.Album;
    [ObservableProperty] private ShareTargetRow? _selectedShareTarget;
    [ObservableProperty] private int _shareExpiryDays = 7;
    [ObservableProperty] private string _shareStatus = string.Empty;

    public ObservableCollection<ShareTargetRow> ShareTargets { get; } = new();
    public ObservableCollection<ShareRow> Shares { get; } = new();

    partial void OnSelectedShareKindChanged(ShareKind value) => ReloadShareTargets();

    private void ReloadShareTargets()
    {
        ShareTargets.Clear();
        switch (SelectedShareKind)
        {
            case ShareKind.Album:
                foreach (var a in _library.Albums)
                    ShareTargets.Add(new ShareTargetRow(a.Key, $"{a.Title} — {a.AlbumArtist}"));
                break;
            case ShareKind.Artist:
                foreach (var a in _library.Artists) ShareTargets.Add(new ShareTargetRow(a.Key, a.Name));
                break;
            case ShareKind.Song:
                foreach (var t in _library.Tracks) ShareTargets.Add(new ShareTargetRow(t.Id, $"{t.Title} — {t.DisplayArtist}"));
                break;
            case ShareKind.Playlist:
                foreach (var p in _playlists.Playlists) ShareTargets.Add(new ShareTargetRow(p.Id, p.Name));
                break;
        }
        SelectedShareTarget = ShareTargets.Count > 0 ? ShareTargets[0] : null;
    }

    [RelayCommand]
    private void CreateShare()
    {
        if (SelectedShareTarget is null)
        {
            ShareStatus = "Choose an item to share.";
            return;
        }
        var lifetime = ShareExpiryDays > 0 ? TimeSpan.FromDays(ShareExpiryDays) : (TimeSpan?)null;
        _shares.Create(SelectedShareKind, SelectedShareTarget.Key, SelectedShareTarget.Label, lifetime);
        RefreshShares();
        ShareStatus = "Share link created and ready to copy below.";
    }

    [RelayCommand]
    private void RevokeShare(ShareRow? row)
    {
        if (row is null) return;
        _shares.Revoke(row.Id);
        RefreshShares();
    }

    private void RefreshShares()
    {
        Shares.Clear();
        foreach (var s in _shares.List())
            Shares.Add(new ShareRow(s.Id, s.Label, s.Kind.ToString(), BuildShareUrl(s.Id),
                s.ExpiresAt is { } e ? $"expires {e.LocalDateTime:g}" : "no expiry"));
    }

    private string BuildShareUrl(string token) =>
        Muselly.App.Services.ShareLinkService.BuildShareUrl(_host.Settings, token, LocalIp);

    // --- Firewall ------------------------------------------------------------------------------------

    public string[] FirewallModeOptions { get; } = { "Off", "Allow listed only", "Block listed" };
    public FirewallScope[] FirewallScopeOptions { get; } = { FirewallScope.Both, FirewallScope.Server, FirewallScope.Web };

    [ObservableProperty] private int _firewallModeIndex;
    [ObservableProperty] private string _newFirewallRule = string.Empty;
    [ObservableProperty] private FirewallScope _newFirewallScope = FirewallScope.Both;
    [ObservableProperty] private string _firewallStatus = string.Empty;

    public ObservableCollection<FirewallRuleRow> FirewallRules { get; } = new();

    private bool _suppressFirewallMode;

    async partial void OnFirewallModeIndexChanged(int value)
    {
        if (_suppressFirewallMode) return;
        await _host.UpdateSettingsAsync(s => s.Firewall.Mode = (FirewallMode)value);
        FirewallStatus = value switch
        {
            (int)FirewallMode.Allow => "Allow mode: only listed addresses can connect.",
            (int)FirewallMode.Block => "Block mode: listed addresses are refused.",
            _ => "Firewall is off."
        };
    }

    [RelayCommand]
    private async Task AddFirewallRule()
    {
        var value = (NewFirewallRule ?? string.Empty).Trim();
        if (!IpFilter.IsValid(value))
        {
            FirewallStatus = "Enter a valid address, CIDR (1.1.1.1/16) or range (1.1.0.0-1.1.255.255).";
            return;
        }

        var scope = NewFirewallScope;
        await _host.UpdateSettingsAsync(s => s.Firewall.Rules.Add(new FirewallRule { Value = value, Scope = scope }));
        NewFirewallRule = string.Empty;
        RefreshFirewall();
        FirewallStatus = $"Added {value}.";
    }

    [RelayCommand]
    private async Task RemoveFirewallRule(FirewallRuleRow? row)
    {
        if (row is null) return;
        await _host.UpdateSettingsAsync(s =>
            s.Firewall.Rules.RemoveAll(r => r.Value == row.Value && r.Scope == row.Scope));
        RefreshFirewall();
    }

    private void RefreshFirewall()
    {
        _suppressFirewallMode = true;
        FirewallModeIndex = (int)_host.Settings.Firewall.Mode;
        _suppressFirewallMode = false;

        FirewallRules.Clear();
        foreach (var r in _host.Settings.Firewall.Rules)
            FirewallRules.Add(new FirewallRuleRow(r.Value, r.Scope));
    }

    // --- Remote servers ------------------------------------------------------------------------------

    public ObservableCollection<RemoteServerRow> Remotes { get; } = new();

    [ObservableProperty] private string _addHost = string.Empty;
    [ObservableProperty] private int _addPort;
    [ObservableProperty] private string _addUsername = string.Empty;
    [ObservableProperty] private string _addPassword = string.Empty;
    [ObservableProperty] private bool _addGuest;
    [ObservableProperty] private bool _addRemember = true;
    [ObservableProperty] private string _connectStatus = string.Empty;
    [ObservableProperty] private bool _isConnecting;

    // Fingerprint verification prompt state.
    [ObservableProperty] private bool _showFingerprintPrompt;
    [ObservableProperty] private string _pendingFingerprint = string.Empty;
    [ObservableProperty] private string _fingerprintPromptMessage = string.Empty;
    private (string Host, int Port, string? User, string? Pass, bool Guest, bool Remember)? _pendingConnect;

    [RelayCommand]
    private async Task Connect()
    {
        if (string.IsNullOrWhiteSpace(AddHost) || AddPort <= 0)
        {
            ConnectStatus = "Enter a host and port.";
            return;
        }
        await DoConnectAsync(AddHost.Trim(), AddPort, AddGuest ? null : AddUsername, AddGuest ? null : AddPassword,
            AddGuest, AddRemember, acceptFingerprint: null);
    }

    private async Task DoConnectAsync(string host, int port, string? user, string? pass, bool guest, bool remember,
        string? acceptFingerprint)
    {
        IsConnecting = true;
        ConnectStatus = $"Connecting to {host}:{port}…";
        try
        {
            var result = await _remotes.ConnectAsync(host, port, user, pass, guest, remember, acceptFingerprint);
            if (result.Success)
            {
                ConnectStatus = $"Connected to {result.Server?.DisplayName ?? host} as {result.Role}.";
                ShowFingerprintPrompt = false;
                _pendingConnect = null;
                AddHost = string.Empty; AddUsername = string.Empty; AddPassword = string.Empty;
            }
            else if (result.FingerprintMismatch)
            {
                _pendingConnect = (host, port, user, pass, guest, remember);
                PendingFingerprint = result.Fingerprint;
                FingerprintPromptMessage =
                    "This server's security fingerprint has changed since you last connected. Only accept if you " +
                    "trust this change (otherwise it could indicate an impersonation attempt).";
                ShowFingerprintPrompt = true;
                ConnectStatus = "Fingerprint changed — verification required.";
            }
            else
            {
                ConnectStatus = result.Error ?? "Connection failed.";
            }
        }
        catch (Exception ex)
        {
            ConnectStatus = ex.Message;
        }
        finally
        {
            IsConnecting = false;
            RefreshRemotes();
        }
    }

    [RelayCommand]
    private async Task AcceptFingerprint()
    {
        if (_pendingConnect is not { } p) { ShowFingerprintPrompt = false; return; }
        ShowFingerprintPrompt = false;
        await DoConnectAsync(p.Host, p.Port, p.User, p.Pass, p.Guest, p.Remember, PendingFingerprint);
    }

    [RelayCommand]
    private void RejectFingerprint()
    {
        ShowFingerprintPrompt = false;
        _pendingConnect = null;
        ConnectStatus = "Connection cancelled.";
    }

    [RelayCommand]
    private async Task ConnectSaved(RemoteServerRow? row)
    {
        if (row is null) return;
        IsConnecting = true;
        ConnectStatus = $"Connecting to {row.Display}…";
        try
        {
            var result = await _remotes.ReconnectAsync(row.Id);
            if (result.Success) ConnectStatus = $"Connected to {row.Display}.";
            else if (result.FingerprintMismatch)
            {
                var s = _remotes.Find(row.Id);
                if (s is not null)
                {
                    _pendingConnect = (s.Host, s.Port, s.Guest ? null : s.Username, s.Guest ? null : s.Password, s.Guest,
                        !string.IsNullOrEmpty(s.Password));
                    PendingFingerprint = result.Fingerprint;
                    FingerprintPromptMessage = "This server's security fingerprint has changed. Accept only if you trust it.";
                    ShowFingerprintPrompt = true;
                }
            }
            else ConnectStatus = result.Error ?? "Connection failed.";
        }
        finally
        {
            IsConnecting = false;
            RefreshRemotes();
        }
    }

    [RelayCommand]
    private async Task Disconnect(RemoteServerRow? row)
    {
        if (row is null) return;
        await _remotes.DisconnectAsync(row.Id);
        ConnectStatus = $"Disconnected from {row.Display}.";
        RefreshRemotes();
    }

    [RelayCommand]
    private async Task Forget(RemoteServerRow? row)
    {
        if (row is null) return;
        await _remotes.ForgetAsync(row.Id);
        RefreshRemotes();
    }

    [RelayCommand]
    private async Task ToggleAutoConnect(RemoteServerRow? row)
    {
        if (row is null) return;
        await _remotes.SetAutoConnectAsync(row.Id, !row.AutoConnect);
        RefreshRemotes();
    }

    private void RefreshRemotes()
    {
        Remotes.Clear();
        foreach (var s in _remotes.Servers)
            Remotes.Add(new RemoteServerRow(s.Id,
                string.IsNullOrWhiteSpace(s.DisplayName) ? $"{s.Host}:{s.Port}" : s.DisplayName,
                $"{s.Host}:{s.Port}", _remotes.IsConnected(s.Id), s.PinnedFingerprint, s.AutoConnect));
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

/// <summary>A row in the server's user list.</summary>
public sealed class ServerUserRow
{
    public ServerUserRow(string username, UserRole role)
    {
        Username = username;
        Role = role;
    }

    public string Username { get; }
    public UserRole Role { get; }
    public string RoleText => Role.ToString();
}

/// <summary>A pickable share target (album/artist/song/playlist).</summary>
public sealed class ShareTargetRow
{
    public ShareTargetRow(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

/// <summary>A row in the existing share-links list.</summary>
public sealed class ShareRow
{
    public ShareRow(string id, string label, string kindText, string url, string expiry)
    {
        Id = id;
        Label = label;
        KindText = kindText;
        Url = url;
        Expiry = expiry;
    }

    public string Id { get; }
    public string Label { get; }
    public string KindText { get; }
    public string Url { get; }
    public string Expiry { get; }
    public string Detail => $"{KindText}  •  {Expiry}";
}

/// <summary>A row in the firewall rules list.</summary>
public sealed class FirewallRuleRow
{
    public FirewallRuleRow(string value, FirewallScope scope)
    {
        Value = value;
        Scope = scope;
    }

    public string Value { get; }
    public FirewallScope Scope { get; }
    public string ScopeText => Scope switch
    {
        FirewallScope.Server => "Server",
        FirewallScope.Web => "Web",
        _ => "Both"
    };
}

/// <summary>A row in the saved remote-servers list.</summary>
public sealed class RemoteServerRow
{
    public RemoteServerRow(string id, string display, string address, bool connected, string fingerprint,
        bool autoConnect)
    {
        Id = id;
        Display = display;
        Address = address;
        Connected = connected;
        Fingerprint = fingerprint;
        AutoConnect = autoConnect;
    }

    public string Id { get; }
    public string Display { get; }
    public string Address { get; }
    public bool Connected { get; }
    public bool NotConnected => !Connected;
    public string Fingerprint { get; }
    public bool AutoConnect { get; }
    public string StatusText => Connected ? "Connected" : "Not connected";
}
