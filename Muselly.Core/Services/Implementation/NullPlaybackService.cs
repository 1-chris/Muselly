using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// A silent playback simulator used until a platform supplies a real audio backend (and on the web demo).
/// It advances a virtual playhead with a timer so the scrobbler, time labels and auto-advance all behave
/// exactly as they will with real audio — there is just no sound.
/// </summary>
public sealed class NullPlaybackService : IPlaybackService, IDisposable
{
    private readonly ILogger<NullPlaybackService> _logger;
    private readonly System.Timers.Timer _timer;
    private readonly object _gate = new();

    public NullPlaybackService(ILogger<NullPlaybackService> logger)
    {
        _logger = logger;
        _timer = new System.Timers.Timer(250) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick();
    }

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public Track? Current { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }
    public double Volume { get; private set; } = 0.8;
    public bool Muted { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler? TrackEnded;

    public void Play(Track track)
    {
        lock (_gate)
        {
            Current = track;
            Position = TimeSpan.Zero;
            Duration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.FromMinutes(3);
            State = PlaybackState.Playing;
            _timer.Start();
        }
        _logger.LogInformation("Simulating playback of '{Title}' (no audio backend)", track.Title);
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestorePaused(Track track, TimeSpan position)
    {
        lock (_gate)
        {
            Current = track;
            Duration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.FromMinutes(3);
            Position = Clamp(position);
            State = PlaybackState.Paused;
            _timer.Stop();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Prepare(Track track) { /* nothing to pre-decode in the simulator */ }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        State = PlaybackState.Paused;
        _timer.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (State != PlaybackState.Paused) return;
        State = PlaybackState.Playing;
        _timer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (State == PlaybackState.Playing) Pause();
        else if (State == PlaybackState.Paused) Resume();
    }

    public void Stop()
    {
        lock (_gate)
        {
            State = PlaybackState.Stopped;
            Position = TimeSpan.Zero;
            _timer.Stop();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate) Position = Clamp(position);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        bool ended = false;
        lock (_gate)
        {
            if (State != PlaybackState.Playing) return;
            Position += TimeSpan.FromMilliseconds(250);
            if (Position >= Duration)
            {
                Position = Duration;
                State = PlaybackState.Stopped;
                _timer.Stop();
                ended = true;
            }
        }
        PositionChanged?.Invoke(this, EventArgs.Empty);
        if (ended) TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan Clamp(TimeSpan p)
    {
        if (p < TimeSpan.Zero) return TimeSpan.Zero;
        return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
    }

    public void Dispose() => _timer.Dispose();
}
