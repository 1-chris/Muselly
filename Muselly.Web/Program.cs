using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Avalonia.Media.Fonts;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

namespace Muselly.Web;

// The shared Application type lives in the Muselly.App namespace; alias it so the bare name `App` here
// doesn't bind to the sibling `Muselly.App` namespace instead of the type.
using SharedApp = Muselly.App.App;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Plug the browser host integration (browser-safe service stubs, single-view shell) into the
        // shared App.
        SharedApp.Platform = new WebPlatform();

        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SharedApp>()
            .WithInterFont()
            // The browser backend renders text with embedded fonts only (no OS fonts), so make Inter the
            // default and add a Japanese-capable fallback so CJK glyphs render instead of tofu boxes.
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter") },
                    new FontFallback { FontFamily = new FontFamily("avares://Muselly.Web/Assets/Fonts/NotoSansJP-Regular.ttf#Noto Sans JP") }
                }
            });
}
