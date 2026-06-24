using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Loads and persists the user <see cref="AppSettings"/>. The current settings are kept in memory and
/// written back to disk on <see cref="Save"/>. <see cref="Changed"/> fires after any mutation so the UI
/// (and other services) can react — e.g. the library rescanning when the folder list changes.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    void Save();

    /// <summary>Mutates settings via the supplied action then persists and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> mutate);

    void AddMusicFolder(string path);

    void RemoveMusicFolder(string path);
}
