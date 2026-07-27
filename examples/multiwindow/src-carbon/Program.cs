using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;
using DotCarbon.Plugins.Window;

var config = ConfigLoader.Load();

// The Window plugin exposes window:* to the frontend, including window:create. The main window's
// app.js opens a second labeled window loading second.html.
CarbonApp.Create(config)
    .UseDesktop()
    .UsePlugin<WindowPlugin>()
    .Run();
