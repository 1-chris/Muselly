using System;
using System.Collections.Generic;

namespace Muselly.Core.Audio;

/// <summary>
/// A graphic equaliser applied to the audio render path. Implementations are real-time safe: the audio
/// thread calls <see cref="Process"/> on each block while the UI thread changes gains/enable. Gains are in
/// decibels per fixed ISO band; a master <see cref="PreampDb"/> compensates for boosted bands. State is
/// persisted via the settings service so the curve survives restarts.
/// </summary>
public interface IAudioEqualizer
{
    bool Enabled { get; set; }

    /// <summary>The fixed band centre frequencies in Hz (for UI labels).</summary>
    IReadOnlyList<int> BandFrequencies { get; }

    int BandCount { get; }

    /// <summary>Master pre-amplification in dB applied before the bands.</summary>
    double PreampDb { get; set; }

    double GetGain(int band);

    /// <summary>Sets a band's gain in dB (clamped to the supported range) and recomputes coefficients.</summary>
    void SetGain(int band, double db);

    /// <summary>Flattens all bands and the preamp to 0 dB.</summary>
    void Reset();

    /// <summary>The device sample rate the filters are tuned for. Set by the playback backend.</summary>
    void SetSampleRate(int sampleRate);

    /// <summary>
    /// Processes one interleaved block in place at the current sample rate. No-op when disabled. Must not
    /// allocate or throw — it runs on the real-time audio thread.
    /// </summary>
    void Process(Span<float> buffer, int channels);

    /// <summary>Raised when the curve changes, so the UI can refresh.</summary>
    event EventHandler? Changed;
}
