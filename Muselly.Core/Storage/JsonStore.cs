using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muselly.Core.Storage;

/// <summary>
/// A tiny, resilient JSON persistence helper: atomic writes (write to a temp file then move) and a
/// load that never throws — a missing or corrupt file yields the supplied default. Used for settings,
/// the library cache and playlists.
/// </summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Load<T>(string path, Func<T> createDefault)
    {
        try
        {
            if (!File.Exists(path)) return createDefault();
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return createDefault();
            return JsonSerializer.Deserialize<T>(json, Options) ?? createDefault();
        }
        catch
        {
            return createDefault();
        }
    }

    /// <summary>
    /// Loads a large file by streaming it (no whole-file string) and, when <paramref name="internStrings"/>
    /// is set, de-duplicating repeated string values so e.g. the same album/artist/genre/codec text is stored
    /// once across thousands of records. Used for the library cache, where this cuts steady-state memory and
    /// the transient load peak substantially. Never throws — a missing/corrupt file yields the default.
    /// </summary>
    public static T LoadStreaming<T>(string path, Func<T> createDefault, bool internStrings = false)
    {
        try
        {
            if (!File.Exists(path)) return createDefault();
            using var stream = File.OpenRead(path);
            if (stream.Length == 0) return createDefault();

            var options = internStrings ? CreateInterningOptions() : Options;
            // Stream the file (blocking on the worker thread that called us) rather than materialising the
            // whole document as a string then parsing it.
            return JsonSerializer.DeserializeAsync<T>(stream, options).AsTask().GetAwaiter().GetResult()
                   ?? createDefault();
        }
        catch
        {
            return createDefault();
        }
    }

    private static JsonSerializerOptions CreateInterningOptions() => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new StringInterningConverter() }
    };

    /// <summary>Collapses duplicate string values read during one deserialization to a single instance (the
    /// pool is discarded afterwards, so it only de-dupes the loaded object graph, not the CLR intern pool).</summary>
    private sealed class StringInterningConverter : JsonConverter<string>
    {
        private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (s is null) return null;
            if (_pool.TryGetValue(s, out var existing)) return existing;
            _pool[s] = s;
            return s;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json);

        // Atomic-ish replace so a crash mid-write never corrupts the live file.
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
