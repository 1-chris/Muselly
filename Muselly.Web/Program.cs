using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

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
            .WithInterFont();
}
