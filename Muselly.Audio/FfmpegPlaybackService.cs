using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Muselly.Audio.Decoding;
using Muselly.Audio.Output;
using Muselly.Core.Audio;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Audio;

/// <summary>
/// Real, cross-platform audio playback with no third-party runtime: files are decoded to interleaved float
/// PCM (WAV directly, everything else via the <c>ffmpeg</c> CLI) and streamed to the OS-native output
/// (CoreAudio / WASAPI / ALSA). The decoded buffer is resampled to the device rate on the fly in the render
/// callback. Transport state and position are reported through <see cref="IPlaybackService"/>; a light timer
/// raises position/track-ended events off the real-time audio thread.
/// </summary>
public sealed class FfmpegPlaybackService : IPlaybackService, IDisposable
{
    private readonly ILogger<FfmpegPlaybackService> _logger;
    private readonly FfmpegDecoder _decoder = new();
    private readonly IAudioOutput _output;
    private readonly IAudioEqualizer _equalizer;
    private readonly IAudioSourceResolver _sourceResolver;
    private int _deviceRate;
    private int _deviceChannels;
    private readonly System.Timers.Timer _timer;

    // Watchdog: the render callback bumps _renderTicks on every device pull. If playback is active but the
    // count stops advancing, the output device has stalled (a known macOS hazard after sleep/wake or a
    // default-device change) — we restart it to recover instead of leaving the user with silence.
    private long _renderTicks;
    private long _lastRenderTicksSeen;
    private DateTime _lastRenderProgressUtc = DateTime.UtcNow;
    private DateTime _watchdogSuppressUntil = DateTime.MinValue;

    private AudioSampleBuffer? _buffer;
    private double _playFrame;
    private double _ratio = 1.0;
    private long _seekTo = -1;
    private int _ended;
    private int _generation;

    // Pre-buffer for the upcoming track so transitions are gapless even when decoding is slow (e.g. a
    // high-bitrate file over the network). We keep at most one prepared track to bound memory.
    private readonly object _cacheLock = new();
    private string? _preparedSource;
    private AudioSampleBuffer? _preparedBuffer;
    private string? _preparingSource;

    private volatile PlaybackState _state = PlaybackState.Stopped;
    private volatile bool _muted;
    private double _volume = 0.8;

    public FfmpegPlaybackService(ILogger<FfmpegPlaybackService> logger, IAudioEqualizer equalizer,
        IAudioSourceResolver sourceResolver)
    {
        _logger = logger;
        _equalizer = equalizer;
        _sourceResolver = sourceResolver;
        _output = AudioOutputFactory.CreateDefault(logger);
        _output.Start(Render);
        _deviceRate = _output.Format.SampleRate <= 0 ? 48000 : _output.Format.SampleRate;
        _deviceChannels = _output.Format.Channels <= 0 ? 2 : _output.Format.Channels;
        _equalizer.SetSampleRate(_deviceRate);

        if (_output is SilentOutput)
            _logger.LogWarning("No native audio device available; playing silently.");
        else
            _logger.LogInformation("Audio output: {Output} @ {Rate} Hz, {Channels} ch",
                _output.GetType().Name, _deviceRate, _deviceChannels);

        if (!FfmpegDecoder.IsFfmpegAvailable())
            _logger.LogWarning(
                "ffmpeg was not found on PATH. WAV files will play; MP3/FLAC/ALAC/AAC/Opus need ffmpeg " +
                "installed (e.g. 'brew install ffmpeg') or an ffmpeg binary shipped next to the app.");

        _timer = new System.Timers.Timer(150) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick();
        _timer.Start();
    }

    public PlaybackState State => _state;
    public Track? Current { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }
    public double Volume => _volume;
    public bool Muted => _muted;

    public event EventHandler? StateChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler? TrackEnded;

    public void Play(Track track)
    {
        var gen = Interlocked.Increment(ref _generation);
        Current = track;
        Position = TimeSpan.Zero;
        Duration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.Zero;
        Interlocked.Exchange(ref _ended, 0);
        Interlocked.Exchange(ref _seekTo, -1);
        _buffer = null;     // silence while (de)coding
        _playFrame = 0;
        _state = PlaybackState.Playing;
        SyncPowerAssertion();
        StateChanged?.Invoke(this, EventArgs.Empty);

        // Use the pre-buffered decode if it's ready — instant, gapless start.
        var prepared = TakePrepared(track.Source);
        if (prepared is not null)
        {
            ApplyBuffer(prepared, gen);
            return;
        }

        Task.Run(() => DecodeAndStart(track, gen));
    }

    public void RestorePaused(Track track, TimeSpan position)
    {
        var gen = Interlocked.Increment(ref _generation);
        Current = track;
        Duration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.Zero;
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        Interlocked.Exchange(ref _ended, 0);
        Interlocked.Exchange(ref _seekTo, -1);
        _buffer = null;
        _playFrame = 0;
        _state = PlaybackState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);

        var startSeconds = Math.Max(0, position.TotalSeconds);
        Task.Run(() =>
        {
            try
            {
                var path = _sourceResolver.ResolveLocalPathAsync(track).GetAwaiter().GetResult();
                var buffer = _decoder.Decode(path);
                if (gen != Volatile.Read(ref _generation)) return; // superseded by a real Play

                _ratio = (double)buffer.SampleRate / _deviceRate;
                Duration = TimeSpan.FromSeconds((double)buffer.FrameCount / buffer.SampleRate);
                var frame = startSeconds * buffer.SampleRate;
                _playFrame = frame >= buffer.FrameCount ? 0 : frame;
                _buffer = buffer; // stays silent until Resume because _state is Paused
                Position = TimeSpan.FromSeconds(_playFrame / buffer.SampleRate);
                StateChanged?.Invoke(this, EventArgs.Empty);
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to restore paused playback for {Source}", track.Source);
            }
        });
    }

    public void Prepare(Track track)
    {
        if (track is null) return;
        var source = track.Source;

        lock (_cacheLock)
        {
            if (_preparedSource == source || _preparingSource == source) return;
            _preparingSource = source;
        }

        Task.Run(() =>
        {
            try
            {
                var path = _sourceResolver.ResolveLocalPathAsync(track).GetAwaiter().GetResult();
                var buffer = _decoder.Decode(path);
                lock (_cacheLock)
                {
                    _preparedSource = source;
                    _preparedBuffer = buffer;
                    _preparingSource = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pre-buffer failed for {Source}", source);
                lock (_cacheLock) { if (_preparingSource == source) _preparingSource = null; }
            }
        });
    }

    private AudioSampleBuffer? TakePrepared(string source)
    {
        lock (_cacheLock)
        {
            if (_preparedSource == source && _preparedBuffer is not null)
            {
                var buf = _preparedBuffer;
                _preparedSource = null;
                _preparedBuffer = null;
                return buf;
            }
            return null;
        }
    }

    private void DecodeAndStart(Track track, int gen)
    {
        try
        {
            var path = _sourceResolver.ResolveLocalPathAsync(track).GetAwaiter().GetResult();
            var buffer = _decoder.Decode(path);
            ApplyBuffer(buffer, gen);
        }
        catch (Exception ex)
        {
            if (gen != Volatile.Read(ref _generation)) return;
            _logger.LogError(ex, "Failed to decode {Source}", track.Source);
            _state = PlaybackState.Stopped;
            Interlocked.Exchange(ref _ended, 1); // let the coordinator skip to the next track
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyBuffer(AudioSampleBuffer buffer, int gen)
    {
        if (gen != Volatile.Read(ref _generation)) return; // superseded by a newer Play
        _ratio = (double)buffer.SampleRate / _deviceRate;
        _playFrame = 0;
        Duration = TimeSpan.FromSeconds((double)buffer.FrameCount / buffer.SampleRate);
        _buffer = buffer; // RT thread starts rendering from here
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (_state != PlaybackState.Playing) return;
        _state = PlaybackState.Paused;
        SyncPowerAssertion();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (_state != PlaybackState.Paused) return;
        _state = PlaybackState.Playing;
        SyncPowerAssertion();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (_state == PlaybackState.Playing) Pause();
        else if (_state == PlaybackState.Paused) Resume();
        else if (Current is not null) Play(Current);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation); // cancel any in-flight decode
        _state = PlaybackState.Stopped;
        _buffer = null;
        _playFrame = 0;
        Position = TimeSpan.Zero;
        SyncPowerAssertion();
        StateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        var buffer = _buffer;
        if (buffer is null) return;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        var frame = (long)(position.TotalSeconds * buffer.SampleRate);
        Interlocked.Exchange(ref _seekTo, frame);
        Position = position;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Real-time render (audio thread) -------------------------------------------------------------

    private void Render(Span<float> buffer)
    {
        // Liveness heartbeat for the watchdog — cheap, lock-free, allowed on the RT thread.
        Interlocked.Increment(ref _renderTicks);

        var dch = _deviceChannels;
        var buf = _buffer;
        if (dch <= 0 || _state != PlaybackState.Playing || buf is null)
        {
            buffer.Clear();
            return;
        }

        var seek = Interlocked.Exchange(ref _seekTo, -1);
        if (seek >= 0) _playFrame = seek;

        var s = buf.Samples;
        var srcCh = buf.Channels;
        var srcFrames = buf.FrameCount;
        var ratio = _ratio;
        var pos = _playFrame;
        var vol = _muted ? 0f : (float)_volume;
        var frames = buffer.Length / dch;

        for (var f = 0; f < frames; f++)
        {
            var outBase = f * dch;
            if (pos >= srcFrames)
            {
                for (var c = 0; c < dch; c++) buffer[outBase + c] = 0f;
                Interlocked.Exchange(ref _ended, 1);
                continue;
            }

            var i0 = (long)pos;
            var frac = (float)(pos - i0);
            var i1 = i0 + 1 < srcFrames ? i0 + 1 : i0;

            var l0 = s[i0 * srcCh];
            var l1 = s[i1 * srcCh];
            var left = (l0 + (l1 - l0) * frac) * vol;

            float right;
            if (srcCh > 1)
            {
                var r0 = s[i0 * srcCh + 1];
                var r1 = s[i1 * srcCh + 1];
                right = (r0 + (r1 - r0) * frac) * vol;
            }
            else
            {
                right = left;
            }

            buffer[outBase] = left;
            if (dch > 1) buffer[outBase + 1] = right;
            for (var c = 2; c < dch; c++) buffer[outBase + c] = 0f;

            pos += ratio;
        }

        _playFrame = pos;

        // Apply the graphic EQ to the filled block (no-op when disabled).
        _equalizer.Process(buffer, dch);
    }

    private void Tick()
    {
        var buf = _buffer;
        if (_state == PlaybackState.Playing && buf is not null)
        {
            Position = TimeSpan.FromSeconds(_playFrame / buf.SampleRate);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        WatchdogCheck();
        SyncPowerAssertion();

        if (Interlocked.CompareExchange(ref _ended, 0, 1) == 1)
        {
            _state = PlaybackState.Stopped;
            if (Duration > TimeSpan.Zero) Position = Duration;
            StateChanged?.Invoke(this, EventArgs.Empty);
            TrackEnded?.Invoke(this, EventArgs.Empty);
            SyncPowerAssertion();
        }
    }

    // --- Output stall recovery -----------------------------------------------------------------------

    /// <summary>
    /// Detects the output device silently stalling while we're actively playing (the render callback stops
    /// being pulled) and restarts it. Catches the macOS cases where an AUHAL goes dead after sleep/wake or a
    /// default-output-device change — which otherwise present as random multi-second dropouts.
    /// </summary>
    private void WatchdogCheck()
    {
        var now = DateTime.UtcNow;

        // Only meaningful while we expect continuous pulls. Otherwise keep the baseline fresh so a long
        // pause/stop never trips the watchdog on resume.
        if (_state != PlaybackState.Playing || _buffer is null)
        {
            _lastRenderTicksSeen = Interlocked.Read(ref _renderTicks);
            _lastRenderProgressUtc = now;
            return;
        }

        if (now < _watchdogSuppressUntil) return; // grace period after a restart while the device spins up

        var ticks = Interlocked.Read(ref _renderTicks);
        if (ticks != _lastRenderTicksSeen)
        {
            _lastRenderTicksSeen = ticks;
            _lastRenderProgressUtc = now;
            return;
        }

        // No render callbacks since the last check — if that persists, the device has stalled.
        if (now - _lastRenderProgressUtc > TimeSpan.FromMilliseconds(700))
        {
            RestartOutput();
            _watchdogSuppressUntil = DateTime.UtcNow.AddMilliseconds(1500);
            _lastRenderProgressUtc = DateTime.UtcNow;
            _lastRenderTicksSeen = Interlocked.Read(ref _renderTicks);
        }
    }

    private void RestartOutput()
    {
        try
        {
            _logger.LogWarning("Audio output appears to have stalled; restarting the device.");
            _output.Stop();
            _output.Start(Render);

            // The restart re-opens the current default device, which may differ from the old one — adopt its
            // format so resampling/EQ stay correct.
            _deviceRate = _output.Format.SampleRate <= 0 ? 48000 : _output.Format.SampleRate;
            _deviceChannels = _output.Format.Channels <= 0 ? 2 : _output.Format.Channels;
            _equalizer.SetSampleRate(_deviceRate);
            var buf = _buffer;
            if (buf is not null) _ratio = (double)buf.SampleRate / _deviceRate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart the audio output after a stall.");
        }
    }

    /// <summary>Holds a macOS "user-initiated" power assertion while playing so App Nap doesn't throttle the
    /// process (and its audio feed/timers) after long periods in the background. Idempotent and no-op elsewhere.</summary>
    private void SyncPowerAssertion()
    {
        if (_state == PlaybackState.Playing) MacAppNap.Begin("Muselly is playing audio");
        else MacAppNap.End();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _output.Dispose();
        MacAppNap.End();
    }
}
