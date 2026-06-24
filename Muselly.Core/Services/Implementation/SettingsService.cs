using Muselly.Core.Models;
using Muselly.Core.Storage;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// File-backed <see cref="ISettingsService"/>. Reads <c>settings.json</c> on construction and writes it
/// back atomically on save. Folder paths are de-duplicated case-insensitively.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly object _gate = new();

    public SettingsService()
    {
        Current = JsonStore.Load(StoragePaths.SettingsFile(), () => new AppSettings());
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_gate) JsonStore.Save(StoragePaths.SettingsFile(), Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(Current);
            JsonStore.Save(StoragePaths.SettingsFile(), Current);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddMusicFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(path);
        Update(s =>
        {
            foreach (var existing in s.MusicFolders)
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase))
                    return;
            s.MusicFolders.Add(full);
        });
    }

    public void RemoveMusicFolder(string path)
    {
        Update(s => s.MusicFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
    }
}
