using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.ViewModels;
using Muselly.Web.Services;

namespace Muselly.Web.ViewModels;

/// <summary>
/// Admin surface for the browser: manage the <em>host's</em> scanned folders and trigger rescans over the
/// web API (these operate on the desktop host, not the browser, which has no filesystem). Only meaningful
/// for admins; the shell only shows it when the signed-in role is Admin.
/// </summary>
public sealed partial class AdminViewModel : ViewModelBase
{
    private readonly WebHostClient _client;
    private readonly WebSession _session;
    private readonly WebLibraryLoader _loader;

    public AdminViewModel(WebHostClient client, WebSession session, WebLibraryLoader loader)
    {
        _client = client;
        _session = session;
        _loader = loader;
    }

    public ObservableCollection<string> Folders { get; } = new();

    [ObservableProperty] private string _newFolderPath = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public bool IsAdmin => _session.IsAdmin;

    public async Task LoadAsync()
    {
        if (!_session.IsAdmin) return;
        var settings = await _client.GetAdminSettingsAsync();
        Folders.Clear();
        if (settings is not null)
            foreach (var f in settings.MusicFolders) Folders.Add(f);
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var path = (NewFolderPath ?? string.Empty).Trim();
        if (path.Length == 0) return;
        if (await _client.AddFolderAsync(path))
        {
            NewFolderPath = string.Empty;
            Status = "Folder added — rescanning that folder on the host…";
            await _client.RescanAsync(path);
            await LoadAsync();
            await RefreshLibrarySoon();
        }
        else
        {
            Status = "Could not add the folder (admin required).";
        }
    }

    [RelayCommand]
    private async Task RemoveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await _client.RemoveFolderAsync(path);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RescanFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Status = "Rescanning folder on the host…";
        await _client.RescanAsync(path);
        await RefreshLibrarySoon();
    }

    [RelayCommand]
    private async Task RescanAll()
    {
        Status = "Rescanning the host library…";
        await _client.RescanAsync();
        await RefreshLibrarySoon();
    }

    private async Task RefreshLibrarySoon()
    {
        // The host scans in the background; give it a moment, then pull the updated library.
        await Task.Delay(2000);
        await _loader.RefreshAsync();
        Status = "Library updated.";
    }
}
