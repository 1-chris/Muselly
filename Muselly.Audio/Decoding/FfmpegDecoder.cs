using System;
using System.Diagnostics;
using System.IO;

namespace Muselly.Audio.Decoding;

/// <summary>
/// Decodes audio files to interleaved float PCM. <c>.wav</c> files are parsed directly (no external tool);
/// every other format (MP3, FLAC, ALAC/AAC in M4A, Opus, Ogg…) is transcoded to a temporary 32-bit-float
/// WAV by the <c>ffmpeg</c> CLI and then parsed. ffmpeg must be on the PATH (or bundled next to the app);
/// it is the only external dependency and ships statically for every desktop OS.
/// </summary>
public sealed class FfmpegDecoder
{
    /// <summary>The ffmpeg executable name/path. Defaults to "ffmpeg" (resolved via PATH).</summary>
    public static string FfmpegPath { get; set; } = "ffmpeg";

    public AudioSampleBuffer Decode(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            using var wav = File.OpenRead(path);
            return WavParser.Parse(wav);
        }

        var temp = Path.Combine(Path.GetTempPath(), $"muselly-{Guid.NewGuid():N}.wav");
        try
        {
            Transcode(path, temp);
            using var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
            return WavParser.Parse(stream);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Returns true if ffmpeg can be launched on this machine.</summary>
    public static bool IsFfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is null) return false;
            p.WaitForExit(4000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Transcode(string input, string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(input);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("wav");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("pcm_f32le");
        psi.ArgumentList.Add(output);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not run ffmpeg — is it installed and on the PATH?", ex);
        }

        using (process)
        {
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"ffmpeg failed to decode '{Path.GetFileName(input)}' (exit {process.ExitCode}): {error.Trim()}");
        }
    }
}
