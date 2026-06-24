namespace Muselly.Audio;

/// <summary>The PCM format a device runs at: sample rate and channel count. Samples are 32-bit float, interleaved.</summary>
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    public static AudioFormat Default => new(48000, 2);
}

/// <summary>
/// Pulls audio for playback: the device repeatedly calls this asking for the next block of interleaved
/// float samples. <paramref name="buffer"/> length = frames × channels. Runs on a real-time audio thread,
/// so the implementation must not allocate, lock heavily, or throw.
/// </summary>
public delegate void AudioRenderCallback(System.Span<float> buffer);

/// <summary>
/// A platform audio output device. The concrete implementations (CoreAudio, WASAPI, ALSA) live alongside;
/// the player depends only on this seam. <see cref="Format"/> is known after <see cref="Start"/>.
/// </summary>
public interface IAudioOutput : System.IDisposable
{
    AudioFormat Format { get; }

    bool IsRunning { get; }

    /// <summary>Opens the default output device and begins streaming, pulling blocks via <paramref name="callback"/>.</summary>
    void Start(AudioRenderCallback callback);

    void Stop();
}
