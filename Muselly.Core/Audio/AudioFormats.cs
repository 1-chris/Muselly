namespace Muselly.Core.Audio;

/// <summary>
/// The audio container/codec formats Muselly recognises while scanning. Extension matching is
/// case-insensitive and includes the leading dot.
/// </summary>
public static class AudioFormats
{
    /// <summary>Supported file extensions (lowercase, with leading dot).</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",   // MPEG-1/2 Audio Layer III
        ".wav",   // Waveform / PCM
        ".flac",  // Free Lossless Audio Codec
        ".m4a",   // MPEG-4 container — holds ALAC (lossless) or AAC
        ".alac",  // Apple Lossless (rare bare extension)
        ".aac",   // raw AAC
        ".opus",  // Opus in Ogg
        ".ogg",   // Vorbis/Opus in Ogg (commonly bundled with the above)
        ".oga"
    };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    /// <summary>A friendly, human-facing list for the settings screen.</summary>
    public static readonly IReadOnlyList<string> DisplayNames = new[]
    {
        "MP3", "WAV", "FLAC", "ALAC", "AAC", "Opus"
    };
}
