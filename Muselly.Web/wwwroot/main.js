import { dotnet } from './_framework/dotnet.js';

// Standard Avalonia browser bootstrap. Program.Main wires up the platform and starts the app, rendering
// into <div id="out">. Once managed Main runs and Avalonia attaches, hide the loading splash.
const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// Remove the splash shortly after the runtime hands off to managed code.
const splash = document.getElementById('loading');
if (splash) setTimeout(() => splash.remove(), 1200);

await dotnetRuntime.runMain(config.mainAssemblyName, []);
