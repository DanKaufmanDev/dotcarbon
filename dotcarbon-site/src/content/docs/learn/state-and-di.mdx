---
title: State & dependency injection
description: Manage application state and services with CarbonApp.
---

Carbon uses Microsoft.Extensions.DependencyInjection. Managed state, registered services, plugins,
and `AppHandle` share one application service provider.

```csharp
var app = CarbonApp.Create(config)
    .UseDesktop()
    .Manage(new SessionState())
    .ConfigureServices(services =>
    {
        services.AddSingleton<Clock>();
        services.AddHttpClient();
    })
    .WithPlugin<AppCommands>();

app.Run();
```

Managed state is registered as a singleton and can be resolved from setup code:

```csharp
.Setup(app =>
{
    var session = app.State<SessionState>();
    session.StartedAt = DateTimeOffset.UtcNow;
})
```

Plugins are constructed through DI:

```csharp
public partial class AppCommands(
    AppHandle app,
    SessionState session,
    Clock clock) : IPlugin
{
    public string Namespace => "app";
}
```

Keep UI-independent business logic in ordinary services. Commands should validate bridge input,
delegate to services, and return serializable results. This keeps the backend testable without a
native window.
