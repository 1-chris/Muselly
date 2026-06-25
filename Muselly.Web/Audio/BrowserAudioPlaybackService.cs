using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;
using Muselly.Web.Services;

namespace Muselly.Web.Audio;

/// <summary>
/// <see cref="IPlaybackService"/> for the browser: streams the host's on-the-fly Opus through a single HTML
/// audio element (the browser decodes Opus natively). Transport maps straight onto the element; position,
/// duration and end-of-track are polled from JS on a lightweight loop. This is the web equivalent of the
/// desktop's ffmpeg backend — same UI, real audio.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserAudioPlaybackService : IPlaybackService
{
    private readonly WebHostClient _client;
    private bool _ready;

    public BrowserAudioPlaybackService(WebHostClient client)
    {
        _client = client;
        _ = RunPositionLoopAsync();
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

    /// <summary>Loads the JS module once; called at startup from the web head.</summary>
    public async Task InitializeAsync()
    {
        await Interop.InitAsync().ConfigureAwait(false);
        Interop.AudioInit();
        Interop.AudioSetVolume(Muted ? 0 : Volume);
        _ready = true;
    }

    public void Play(Track track)
    {
        Current = track;
        Position = TimeSpan.Zero;
        Duration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.Zero;
        State = PlaybackState.Playing;

        if (_ready)
        {
            var url = _client.StreamUrl(HostTrackId(track), 0);
            Interop.AudioPlay(url);
            Interop.AudioSetVolume(Muted ? 0 : Volume);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Prepare(Track track) { /* the browser buffers the element itself */ }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        State = PlaybackState.Paused;
        if (_ready) Interop.AudioPause();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (State != PlaybackState.Paused) return;
        State = PlaybackState.Playing;
        if (_ready) Interop.AudioResume();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (State == PlaybackState.Playing) Pause();
        else if (State == PlaybackState.Paused) Resume();
    }

    public void Stop()
    {
        State = PlaybackState.Stopped;
        Position = TimeSpan.Zero;
        if (_ready) Interop.AudioStop();
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (_ready) Interop.AudioSeek(Position.TotalSeconds);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        if (_ready) Interop.AudioSetVolume(Muted ? 0 : Volume);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        if (_ready) Interop.AudioSetVolume(Muted ? 0 : Volume);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunPositionLoopAsync()
    {
        while (true)
        {
            await Task.Delay(250).ConfigureAwait(true);
            if (!_ready || State != PlaybackState.Playing) continue;

            if (Interop.AudioEnded())
            {
                State = PlaybackState.Stopped;
                StateChanged?.Invoke(this, EventArgs.Empty);
                TrackEnded?.Invoke(this, EventArgs.Empty);
                continue;
            }

            Position = TimeSpan.FromSeconds(Interop.AudioGetTime());
            var dur = Interop.AudioGetDuration();
            if (dur > 0) Duration = TimeSpan.FromSeconds(dur);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The host's own track id, recovered from the track's <c>muselly://host/track/&lt;id&gt;</c> source.</summary>
    private static string HostTrackId(Track track) =>
        RemoteSource.TryParse(track.Source, out _, out _, out var key) ? key : track.Id;
}
