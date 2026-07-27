using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;
using DotCarbon.Plugins.Sql;

var config = ConfigLoader.Load();

// The whole to-do backend is the SQL plugin — the frontend talks to it over sql:* commands. The
// database file lives in the app's data directory (see `sqlite:todos.db` in ui/dist/app.js).
CarbonApp.Create(config)
    .UseDesktop()
    .UsePlugin<SqlPlugin>()
    .Run();
