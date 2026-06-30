using System;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.Services;

/// <summary>
/// Watches playback and submits scrobbles for the current user. Sends a "now playing" update when a track
/// starts, and scrobbles a track once it's been played enough (the Last.fm rule: tracks longer than 30s,
/// after half their length or 4 minutes — whichever comes first). Scrobbling for whoever is signed in
/// (the built-in user on the desktop), to their connected Last.fm / ListenBrainz / Libre.fm accounts.
/// </summary>
public sealed class ScrobbleCoordinator
{
    private const double MinTrackSeconds = 30;
    private const double MaxThresholdSeconds = 240; // 4 minutes

    private readonly IPlaybackService _playback;
    private readonly IScrobbleService _scrobble;
    private readonly IUserService _users;
    private readonly object _gate = new();

    private string? _currentId;
    private Track? _currentTrack;
    private DateTimeOffset _startedAt;
    private double _maxPositionSeconds;
    private bool _scrobbled;

    public ScrobbleCoordinator(IPlaybackService playback, IScrobbleService scrobble, IUserService users)
    {
        _playback = playback;
        _scrobble = scrobble;
        _users = users;

        _playback.StateChanged += (_, _) => OnState();
        _playback.PositionChanged += (_, _) => OnPosition();
        _playback.TrackEnded += (_, _) => OnEnded();
    }

    private string CurrentUser => _users.Current.Username;

    private void OnState()
    {
        var track = _playback.Current;
        if (track is null) return;

        // New track? Send now-playing and reset the play-progress tracking.
        if (!string.Equals(track.Id, _currentId, StringComparison.Ordinal))
        {
            lock (_gate)
            {
                _currentId = track.Id;
                _currentTrack = track;
                _startedAt = DateTimeOffset.Now;
                _maxPositionSeconds = 0;
                _scrobbled = false;
            }
            if (_playback.State == PlaybackState.Playing)
                _ = _scrobble.UpdateNowPlayingAsync(CurrentUser, track);
        }
    }

    private void OnPosition()
    {
        Track? track;
        DateTimeOffset startedAt;
        bool eligible;
        lock (_gate)
        {
            if (_currentTrack is null || _scrobbled) return;
            var pos = _playback.Position.TotalSeconds;
            if (pos > _maxPositionSeconds) _maxPositionSeconds = pos;

            track = _currentTrack;
            startedAt = _startedAt;
            eligible = IsEligible(track, _maxPositionSeconds);
            if (eligible) _scrobbled = true; // submit once
        }

        if (eligible && track is not null)
            _ = _scrobble.ScrobbleAsync(CurrentUser, track, startedAt);
    }

    private void OnEnded()
    {
        Track? track;
        DateTimeOffset startedAt;
        bool submit;
        lock (_gate)
        {
            // A track that played to its end is always eligible (if it's long enough), even if PositionChanged
            // didn't tick close enough to the threshold.
            submit = _currentTrack is not null && !_scrobbled &&
                     (_currentTrack.Duration.TotalSeconds >= MinTrackSeconds || _maxPositionSeconds >= MinTrackSeconds);
            track = _currentTrack;
            startedAt = _startedAt;
            if (submit) _scrobbled = true;
        }

        if (submit && track is not null)
            _ = _scrobble.ScrobbleAsync(CurrentUser, track, startedAt);
    }

    private static bool IsEligible(Track track, double playedSeconds)
    {
        var duration = track.Duration.TotalSeconds;
        if (duration < MinTrackSeconds) return false;
        var threshold = Math.Min(duration / 2.0, MaxThresholdSeconds);
        return playedSeconds >= threshold;
    }
}
