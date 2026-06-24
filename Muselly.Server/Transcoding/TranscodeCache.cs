using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Muselly.Server.Transcoding;

/// <summary>
/// A size-bounded, on-disk LRU cache of transcoded Opus files keyed by track id + bitrate. Concurrent
/// requests for the same item share a single transcode; total size is kept under a configurable cap by
/// evicting the least-recently-used files.
/// </summary>
public sealed class TranscodeCache
{
    private readonly string _directory;
    private readonly OpusTranscoder _transcoder;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly object _evictGate = new();

    public TranscodeCache(string directory, OpusTranscoder transcoder, ILogger logger)
    {
        _directory = directory;
        _transcoder = transcoder;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Maximum total cache size in bytes. Updated live from server settings.</summary>
    public long MaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Returns the path to a cached Opus file for the track, transcoding from <paramref name="sourcePath"/>
    /// on a miss. Returns null if transcoding fails.
    /// </summary>
    public async Task<string?> GetOrCreateAsync(string trackId, int bitrateKbps, string sourcePath,
        CancellationToken ct = default)
    {
        bitrateKbps = OpusTranscoder.ClampBitrate(bitrateKbps);
        var key = $"{trackId}_{bitrateKbps}";
        var path = Path.Combine(_directory, key + ".opus");

        if (File.Exists(path))
        {
            Touch(path);
            return path;
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) { Touch(path); return path; }

            var temp = path + ".tmp";
            try
            {
                await _transcoder.TranscodeAsync(sourcePath, temp, bitrateKbps, ct).ConfigureAwait(false);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Opus transcode failed for {Track}", trackId);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* ignore */ }
                return null;
            }

            EvictIfNeeded();
            return path;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { /* best-effort */ }
    }

    private void EvictIfNeeded()
    {
        lock (_evictGate)
        {
            try
            {
                var files = new DirectoryInfo(_directory).GetFiles("*.opus")
                    .OrderBy(f => f.LastAccessTimeUtc)
                    .ToList();
                var total = files.Sum(f => f.Length);
                var index = 0;
                while (total > MaxBytes && index < files.Count)
                {
                    var f = files[index++];
                    try { total -= f.Length; f.Delete(); }
                    catch { /* file may be in use; skip */ }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Transcode cache eviction failed.");
            }
        }
    }
}
