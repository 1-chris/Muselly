using System.Globalization;
using System.Text.RegularExpressions;
using Muselly.Core.Models;

namespace Muselly.Core.Services.Web;

/// <summary>
/// Parses LRC-format lyrics (the de-facto standard used by embedded synced lyrics and lrclib.net). Handles
/// multiple timestamps per line, metadata tags (<c>[ar:]</c>, <c>[ti:]</c>, …, which are skipped) and
/// enhanced word-level <c>&lt;mm:ss.xx&gt;</c> tags (which are stripped). Plain text without timestamps is
/// returned as untimed lines.
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimeTagRegex();

    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled)]
    private static partial Regex WordTagRegex();

    /// <summary>True if the text looks like LRC (contains at least one <c>[mm:ss]</c> time tag).</summary>
    public static bool LooksSynced(string? text) =>
        !string.IsNullOrEmpty(text) && TimeTagRegex().IsMatch(text);

    /// <summary>Parses LRC (or plain) text into a list of lines, sorted by time when synced.</summary>
    public static List<LyricsLine> Parse(string? text)
    {
        var lines = new List<LyricsLine>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var synced = false;
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var matches = TimeTagRegex().Matches(rawLine);
            if (matches.Count == 0)
            {
                var plain = rawLine.Trim();
                // Skip pure metadata tags like [ar:Artist] when no time tag is present.
                if (plain.Length == 0 || (plain.StartsWith('[') && plain.EndsWith(']') && plain.Contains(':')))
                    continue;
                lines.Add(new LyricsLine { Text = StripWordTags(plain) });
                continue;
            }

            synced = true;
            // Text after the last time tag.
            var lastTag = matches[^1];
            var content = rawLine[(lastTag.Index + lastTag.Length)..].Trim();
            content = StripWordTags(content);

            foreach (Match m in matches)
            {
                var ms = ToMilliseconds(m);
                lines.Add(new LyricsLine { Text = content, TimeMs = ms });
            }
        }

        if (synced)
        {
            // Keep only timed lines and order them; allow blank lines (instrumental breaks) through.
            lines.Sort((a, b) => Nullable.Compare(a.TimeMs, b.TimeMs));
        }

        return lines;
    }

    private static int ToMilliseconds(Match m)
    {
        var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var fraction = 0;
        if (m.Groups[3].Success)
        {
            var frac = m.Groups[3].Value;
            // Normalise to milliseconds (2 digits = centiseconds, 3 digits = ms).
            if (frac.Length == 1) fraction = int.Parse(frac, CultureInfo.InvariantCulture) * 100;
            else if (frac.Length == 2) fraction = int.Parse(frac, CultureInfo.InvariantCulture) * 10;
            else fraction = int.Parse(frac[..3], CultureInfo.InvariantCulture);
        }
        return (minutes * 60 + seconds) * 1000 + fraction;
    }

    private static string StripWordTags(string text) => WordTagRegex().Replace(text, string.Empty).Trim();
}
