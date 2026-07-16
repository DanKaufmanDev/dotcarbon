---
title: Author a plugin
description: Build a reusable DotCarbon plugin with commands, metadata, permissions, and lifecycle hooks.
---

Reference `DotCarbon.Core`, mark the plugin class `partial`, and implement `IPlugin`:

```csharp
[CarbonPlugin("Example", version: "1.0.0", description: "Example integration")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission(
    "example:default",
    "Allow example commands",
    Commands = ["example:*"])]
[CarbonEvent("example:ready", "ExampleReady")]
public partial class ExamplePlugin : IPlugin
{
    private AppHandle? _app;

    public string Namespace => "example";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _app = context.App;
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("ping")]
    public string Ping() => "pong";

    public async ValueTask OnLifecycleAsync(CarbonLifecycleEvent evt)
    {
        if (evt.Kind == CarbonLifecycleEventKind.Ready)
            await evt.App.EmitAsync(new CarbonEventName<string>("example:ready"), "ready");
    }

    public ValueTask DisposeAsync()
    {
        _app = null;
        return ValueTask.CompletedTask;
    }
}
```

## Configuration

Plugin configuration is the JSON value under `plugins.<namespace>`. Deserialize it during
`InitializeAsync`:

```csharp
var options = context.GetConfiguration(ExampleJsonContext.Default.ExampleOptions);
```

Use a source-generated `JsonSerializerContext` for trim and NativeAOT compatibility.

## Generated metadata

The source generator supplies command registration and metadata. Runtime consumers can inspect
`AppHandle.Plugins`; `carbon types` emits matching TypeScript metadata declarations.

## Packaging

A complete third-party integration normally publishes:

- A NuGet package containing the C# plugin and its serializer context.
- An npm package with typed wrappers and `CarbonCommands` / `CarbonEvents` augmentation.
- Capability identifiers and resource scope documentation.
- A supported-platform declaration and tests for each advertised host.

Keep defaults narrow. A permission describes the allowed commands; the plugin must still enforce its
own URL, path, process, or device scope.
