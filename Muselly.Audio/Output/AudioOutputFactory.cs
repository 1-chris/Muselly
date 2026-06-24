using System;
using Microsoft.Extensions.Logging;

namespace Muselly.Audio.Output;

/// <summary>
/// Picks and opens the right native audio output for the current OS (CoreAudio on macOS, WASAPI on
/// Windows, ALSA on Linux). If the native device can't be created, returns a <see cref="SilentOutput"/> so
/// the player keeps working (silently) rather than failing.
/// </summary>
public static class AudioOutputFactory
{
    public static IAudioOutput CreateDefault(ILogger? logger = null)
    {
        try
        {
            if (OperatingSystem.IsMacOS() && CoreAudioInterop.TryProbe())
                return new MacAudioOutput();

            if (OperatingSystem.IsWindows())
                return new WasapiOutput();

            if (OperatingSystem.IsLinux() && AlsaInterop.TryProbe())
                return new AlsaOutput();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Native audio output unavailable — using silent fallback");
        }

        return new SilentOutput();
    }
}
