using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Controls audio output for a single track at a time: transport (play/pause/stop), seeking, volume and
/// progress reporting. The queue/transport coordination lives a layer up; this interface is purely "render
/// this track and tell me where you are". Platform heads provide a real backend; the portable default is a
/// silent simulator so the whole UI is exercised everywhere.
/// </summary>
public interface IPlaybackService
{
    PlaybackState State { get; }

    Track? Current { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>Output volume, 0.0–1.0.</summary>
    double Volume { get; }

    bool Muted { get; }

    /// <summary>Raised when <see cref="State"/> or <see cref="Current"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised frequently while playing as <see cref="Position"/> advances.</summary>
    event EventHandler? PositionChanged;

    /// <summary>Raised when the current track plays through to its end (the coordinator advances the queue).</summary>
    event EventHandler? TrackEnded;

    void Play(Track track);

    /// <summary>
    /// Loads <paramref name="track"/> and holds it <b>paused</b> at <paramref name="position"/> without
    /// starting playback — used to restore the previous session on launch so the user resumes exactly where
    /// they left off. Backends that can't seek precisely may approximate.
    /// </summary>
    void RestorePaused(Track track, TimeSpan position);

    /// <summary>
    /// Hints that <paramref name="track"/> will likely play next, so the backend can pre-decode/buffer it
    /// for a gapless transition. May be a no-op.
    /// </summary>
    void Prepare(Track track);

    void Pause();

    void Resume();

    void TogglePlayPause();

    void Stop();

    void Seek(TimeSpan position);

    void SetVolume(double volume);

    void SetMuted(bool muted);
}
