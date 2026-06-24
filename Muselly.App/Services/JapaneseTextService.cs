using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kawazu;
using Microsoft.Extensions.Logging;

namespace Muselly.App.Services;

/// <summary>A run of text with an optional reading (furigana) shown above it. Empty <see cref="Ruby"/>
/// means the run is kana/punctuation that needs no reading.</summary>
public sealed class FuriganaSegment
{
    public required string Text { get; init; }
    public string? Ruby { get; init; }
}

/// <summary>The romanised form and furigana segmentation of a single line of Japanese text.</summary>
public sealed class JapaneseLine
{
    public required string Romaji { get; init; }
    public required IReadOnlyList<FuriganaSegment> Furigana { get; init; }
}

/// <summary>Converts Japanese text (kana/kanji) to romaji and furigana for the lyrics panel.</summary>
public interface IJapaneseTextService
{
    /// <summary>True if the text contains any Japanese characters (kana or kanji).</summary>
    bool ContainsJapanese(string? text);

    /// <summary>Converts a line to romaji + furigana segments. Returns the original on failure.</summary>
    Task<JapaneseLine> ConvertLineAsync(string text);
}

/// <summary>
/// Japanese text service backed by Kawazu (NMeCab + the IPADIC dictionary bundled with the app). The
/// converter and its dictionary are loaded lazily on first use — so users with no Japanese music never pay
/// the memory cost — and access is serialised because the underlying tagger is not re-entrant.
/// </summary>
public sealed class JapaneseTextService : IJapaneseTextService
{
    private readonly ILogger<JapaneseTextService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KawazuConverter? _converter;
    private bool _initFailed;

    public JapaneseTextService(ILogger<JapaneseTextService> logger) => _logger = logger;

    public bool ContainsJapanese(string? text) =>
        !string.IsNullOrEmpty(text) && Utilities.HasJapanese(text);

    public async Task<JapaneseLine> ConvertLineAsync(string text)
    {
        var fallback = new JapaneseLine
        {
            Romaji = text,
            Furigana = new[] { new FuriganaSegment { Text = text } }
        };

        if (string.IsNullOrWhiteSpace(text) || !ContainsJapanese(text))
            return fallback;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var converter = GetConverter();
            if (converter is null) return fallback;

            var romaji = await converter.Convert(text, To.Romaji, Mode.Spaced).ConfigureAwait(false);
            var divisions = await converter.GetDivisions(text, To.Hiragana).ConfigureAwait(false);

            var segments = new List<FuriganaSegment>(divisions.Count);
            foreach (var d in divisions)
            {
                var surface = d.Surface ?? string.Empty;
                // Only show a reading over runs that actually contain kanji.
                var ruby = Utilities.HasKanji(surface) && !string.IsNullOrEmpty(d.HiraReading) ? d.HiraReading : null;
                segments.Add(new FuriganaSegment { Text = surface, Ruby = ruby });
            }

            return new JapaneseLine
            {
                Romaji = string.IsNullOrWhiteSpace(romaji) ? text : romaji.Trim(),
                Furigana = segments.Count > 0 ? segments : fallback.Furigana
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Japanese conversion failed");
            return fallback;
        }
        finally
        {
            _gate.Release();
        }
    }

    private KawazuConverter? GetConverter()
    {
        if (_converter is not null) return _converter;
        if (_initFailed) return null;
        try
        {
            var dic = Path.Combine(AppContext.BaseDirectory, "IpaDic");
            _converter = new KawazuConverter(dic);
            return _converter;
        }
        catch (Exception ex)
        {
            _initFailed = true;
            _logger.LogWarning(ex, "Japanese dictionary unavailable; romaji/furigana disabled");
            return null;
        }
    }
}
