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
