using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Muselly.Web.Audio;

/// <summary>
/// Managed bindings to <c>wwwroot/interop.js</c>: the page origin and the shared HTMLAudioElement used to
/// stream Opus from the host. <see cref="InitAsync"/> must complete before any other member is called.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class Interop
{
    // The runtime resolves module URLs relative to the _framework/ directory, but interop.js is deployed at
    // the app root (next to index.html), so step up one level.
    public static Task InitAsync() => JSHost.ImportAsync("interop", "../interop.js");

    [JSImport("getOrigin", "interop")] public static partial string GetOrigin();
    [JSImport("getPath", "interop")] public static partial string GetPath();
    [JSImport("setPath", "interop")] public static partial void SetPath(string path);

    [JSImport("audioInit", "interop")] public static partial void AudioInit();
    [JSImport("audioPlay", "interop")] public static partial void AudioPlay(string url);
    [JSImport("audioPause", "interop")] public static partial void AudioPause();
    [JSImport("audioResume", "interop")] public static partial void AudioResume();
    [JSImport("audioStop", "interop")] public static partial void AudioStop();
    [JSImport("audioSeek", "interop")] public static partial void AudioSeek(double seconds);
    [JSImport("audioSetVolume", "interop")] public static partial void AudioSetVolume(double volume);
    [JSImport("audioGetTime", "interop")] public static partial double AudioGetTime();
    [JSImport("audioGetDuration", "interop")] public static partial double AudioGetDuration();
    [JSImport("audioEnded", "interop")] public static partial bool AudioEnded();
}
