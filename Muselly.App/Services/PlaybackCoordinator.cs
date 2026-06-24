using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.Services;

/// <summary>
/// Ties the queue and the audio backend together into a single transport. View models call into this to
/// "play these tracks" or drive prev/next/seek; it owns the auto-advance logic (when a track ends it pulls
/// the next one from the queue) and keeps the backend volume/repeat in sync with settings. This is the one
/// place that knows about both <see cref="IQueueService"/> and <see cref="IPlaybackService"/>.
/// </summary>
public sealed class PlaybackCoordinator
{
    private readonly IQueueService _queue;
    private readonly IPlaybackService _playback;
    private readonly ISettingsService _settings;

    public PlaybackCoordinator(IQueueService queue, IPlaybackService playback, ISettingsService settings)
    {
        _queue = queue;
        _playback = playback;
        _settings = settings;

        // Restore persisted transport state.
        _queue.RepeatMode = settings.Current.RepeatMode;
        _queue.Shuffle = settings.Current.ShuffleEnabled;
        _playback.SetVolume(settings.Current.Volume);
        _playback.SetMuted(settings.Current.Muted);

        _playback.TrackEnded += OnTrackEnded;
        // Re-evaluate the pre-buffer whenever the queue changes (e.g. "play next" was used).
        _queue.QueueChanged += (_, _) => PrepareNext();
    }

    public IQueueService Queue => _queue;
    public IPlaybackService Playback => _playback;

    /// <summary>Replaces the queue with <paramref name="tracks"/> and starts playing from the given index.</summary>
    public void Play(IReadOnlyList<Track> tracks, int startIndex = 0)
    {
        if (tracks.Count == 0) return;
        _queue.PlayNow(tracks, startIndex);
        PlayCurrent();
    }

    /// <summary>Replaces the queue with a shuffled copy of <paramref name="tracks"/> and starts playing.</summary>
    public void PlayShuffled(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        _queue.PlayShuffled(tracks);
        PlayCurrent();
    }

    public void PlayCurrent()
    {
        var current = _queue.Current;
        if (current is not null) _playback.Play(current);
        PrepareNext();
    }

    /// <summary>Pre-decodes the upcoming track so the next transition is gapless.</summary>
    private void PrepareNext()
    {
        var next = _queue.PeekNext();
        if (next is not null) _playback.Prepare(next);
    }

    public void TogglePlayPause()
    {
        if (_playback.Current is null)
        {
            PlayCurrent();
            return;
        }
        _playback.TogglePlayPause();
    }

    public void Next()
    {
        var next = _queue.MoveNext();
        if (next is not null) _playback.Play(next);
        else _playback.Stop();
        PrepareNext();
    }

    public void Previous()
    {
        // Restart the current track if we're more than a few seconds in (familiar player behaviour).
        if (_playback.Position > TimeSpan.FromSeconds(3))
        {
            _playback.Seek(TimeSpan.Zero);
            return;
        }
        var prev = _queue.MovePrevious();
        if (prev is not null) _playback.Play(prev);
        else _playback.Seek(TimeSpan.Zero);
        PrepareNext();
    }

    public void PlayQueueIndex(int index)
    {
        _queue.SetCurrentIndex(index);
        PlayCurrent();
    }

    public void Seek(TimeSpan position) => _playback.Seek(position);

    public void SetVolume(double volume)
    {
        _playback.SetVolume(volume);
        _settings.Update(s => s.Volume = volume);
    }

    public void ToggleMute()
    {
        var muted = !_playback.Muted;
        _playback.SetMuted(muted);
        _settings.Update(s => s.Muted = muted);
    }

    public void CycleRepeat()
    {
        _queue.RepeatMode = _queue.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        _settings.Update(s => s.RepeatMode = _queue.RepeatMode);
    }

    public void ToggleShuffle()
    {
        _queue.Shuffle = !_queue.Shuffle;
        _settings.Update(s => s.ShuffleEnabled = _queue.Shuffle);
    }

    private void OnTrackEnded(object? sender, EventArgs e)
    {
        // The backend raises this off its own thread; hop to the UI thread before touching the queue/UI.
        Dispatcher.UIThread.Post(Next);
    }
}
