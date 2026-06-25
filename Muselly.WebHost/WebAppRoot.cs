namespace Muselly.WebHost;

/// <summary>
/// Locates the published browser app (the Avalonia <c>browser-wasm</c> <c>AppBundle</c>) that the web server
/// serves as static files. Resolution order: the <c>MUSELLY_WEBROOT</c> environment variable, then a
/// <c>webapp</c> folder next to the running binary. Returns null if nothing suitable is found, in which case
/// the API still works but the SPA isn't served.
/// </summary>
public static class WebAppRoot
{
    public const string EnvironmentVariable = "MUSELLY_WEBROOT";

    public static string? Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv) && LooksLikeBundle(fromEnv)) return Path.GetFullPath(fromEnv);

        var candidate = Path.Combine(AppContext.BaseDirectory, "webapp");
        if (LooksLikeBundle(candidate)) return candidate;

        return null;
    }

    private static bool LooksLikeBundle(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, "index.html"));
}
