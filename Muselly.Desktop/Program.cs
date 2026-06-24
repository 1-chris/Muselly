using Avalonia;
using System;

namespace Muselly.Desktop;

// The shared Application type lives in the Muselly.App namespace; alias it so the bare name `App` here
// doesn't bind to the sibling `Muselly.App` namespace instead of the type.
using SharedApp = Muselly.App.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Plug the desktop host integration into the shared App, then run the classic multi-window
        // desktop lifetime.
        SharedApp.Platform = new DesktopPlatform();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SharedApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
