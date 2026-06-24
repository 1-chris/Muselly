using System.Diagnostics;

namespace Muselly.Server.Transcoding;

/// <summary>
/// Transcodes any source file to Opus (in an Ogg container) using the bundled ffmpeg CLI. The desktop head
/// points <see cref="FfmpegPath"/> at the same statically-built binary the player uses for decoding (which
/// is configured with libopus). Bitrate is clamped to the supported 64-256 kbps range.
/// </summary>
public sealed class OpusTranscoder
{
    public const int MinBitrateKbps = 64;
    public const int MaxBitrateKbps = 256;

    /// <summary>The ffmpeg executable path. Defaults to resolving "ffmpeg" on PATH.</summary>
    public static string FfmpegPath { get; set; } = "ffmpeg";

    public static int ClampBitrate(int kbps) => Math.Clamp(kbps, MinBitrateKbps, MaxBitrateKbps);

    public async Task TranscodeAsync(string inputPath, string outputPath, int bitrateKbps, CancellationToken ct = default)
    {
        bitrateKbps = ClampBitrate(bitrateKbps);

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
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("libopus");
        psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add($"{bitrateKbps}k");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("ogg");
        psi.ArgumentList.Add(outputPath);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not run ffmpeg for Opus transcoding.", ex);
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"ffmpeg failed to transcode '{Path.GetFileName(inputPath)}' (exit {process.ExitCode}): {error.Trim()}");
        }
    }
}
