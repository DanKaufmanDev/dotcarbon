<div align="center">

# DotCarbon

**Build fast, tiny, cross-platform desktop apps with C# and web technologies.**

Write your interface with any web framework, your application logic in C#, and ship a single native binary — no runtime to install, no bloat.

[![NuGet](https://img.shields.io/nuget/v/DotCarbon.Core?logo=nuget&label=DotCarbon.Core)](https://www.nuget.org/packages/DotCarbon.Core)
[![npm](https://img.shields.io/npm/v/%40dotcarbon%2Fcreate-app?logo=npm&label=create-app)](https://www.npmjs.com/package/@dotcarbon/create-app)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/DanKaufmanDev/dotcarbon/blob/main/LICENSE)

</div>

---

DotCarbon lets you build native desktop applications using the web stack you already know for the UI and the full power of .NET for everything else. Your interface renders in the operating system's built-in web view, and your C# code runs natively alongside it — the two talk over a small, fully typed bridge.

> **Status:** pre-release (`0.1.x`). The API is still stabilizing and may change between minor versions.

## Highlights

- **Single-file apps** — The .NET runtime, native webview host, configuration, and compiled frontend ship as one compressed executable.
- **Any frontend** — First-class templates for **React, Vue, Svelte, Solid, Preact, and Vanilla**, all TypeScript + Vite.
- **End-to-end type safety** — Your C# commands are projected into TypeScript types, so `invoke()` calls are autocompleted and checked at compile time.
- **C# backend** — Use the entire .NET ecosystem for your application logic, file access, networking, and native OS integration.
- **Application runtime** — Managed state and DI, labeled multi-window apps, lifecycle hooks, and typed cross-webview events.
- **Production permissions** — Tauri-style capability files restrict which commands each window can call.
- **NuGet-ready plugins** — Plugins can initialize, read config, emit events, declare permissions, handle lifecycle, and expose generated metadata.
- **Built-in capabilities** — Clipboard, dialogs, file system, notifications, shell, window management, Store, Opener, DeepLink, SingleInstance, GlobalShortcut, and Updater, available as opt-in plugins.
- **Batteries-included CLI** — One command to develop, one to ship.

## Quick start

```bash
npx @dotcarbon/create-app my-app
cd my-app
carbon dev
```

Pick a template with `--template`:

```bash
npx @dotcarbon/create-app my-app --template vue
```

Available templates: `react` · `vue` · `svelte` · `solid` · `preact` · `vanilla`.

`carbon dev` starts your frontend dev server and the C# host together with live reload. When you're ready to distribute, `carbon build` compiles a native app into `out/`.

## Requirements

| Tool | Version | Notes |
|------|---------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10+ | Runs the host and the CLI |
| [Node.js](https://nodejs.org) | 18+ | With pnpm, npm, bun, or yarn |
| `carbon` CLI | latest | `dotnet tool install -g DotCarbon.Cli` |

The scaffolder detects the `carbon` CLI and offers to install it for you if it's missing.

## How it works

A DotCarbon app is two halves that share one window:

```
my-app/
├─ carbon.json          app, window, and build configuration
├─ carbon.schema.json   schema for carbon.json (editor autocomplete)
├─ src-carbon/          C# backend — the host and your commands
│  └─ Program.cs
└─ ui/                  web frontend (Vite)
   └─ src/
```

The C# host loads your web UI into the native OS web view. The frontend and backend communicate through a bridge: the frontend calls a **command** by name, a C# method handles it, and the typed result is returned as a promise.

### Calling C# from the frontend

Define a command in C# — mark a method with `[CarbonCommand]` inside a plugin:

```csharp
// src-carbon/Program.cs
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;

var config = ConfigLoader.Load();

new CarbonHost(config)
    .WithPlugin(new AppCommands())
    .Run();

public record GreetRequest(string Name);

public partial class AppCommands : IPlugin
{
    public string Namespace => "app";

    [CarbonCommand("greet")]
    public string Greet(GreetRequest req) => $"Hello, {req.Name}!";
}
```

Call it from the frontend with a fully typed `invoke`:

```ts
import { invoke } from '@dotcarbon/api'

const message = await invoke('app:greet', { name: 'World' })
// message: string  →  "Hello, World!"
```

`carbon dev` automatically generates and watches `ui/src/carbon.d.ts` from your commands — arguments and return values become real TypeScript types, so typos and mismatched payloads are caught before you run. It also keeps local app commands synced into `src-carbon/capabilities/main.json`, so normal `[CarbonCommand]` methods work with security enabled. Use `carbon types` when you want a one-shot generation and capability sync step for CI or scripts.

For application state, dependency injection, additional windows, lifecycle events, and the typed event
bus, see [Application runtime](RUNTIME.md).

For production command allowlists, per-window permissions, and Tauri-style
`src-carbon/capabilities/*.json` files, see [Capabilities and permissions](CAPABILITIES.md).

For secure defaults, bridge hardening, CSP, trusted origins, and the hardening roadmap, see
[Security and hardening](SECURITY.md).

For plugin authoring, NuGet package guidance, lifecycle hooks, plugin config, and generated metadata,
see [Plugin contract v2](PLUGINS.md).

## CLI

| Command | Description |
|---------|-------------|
| `carbon dev` | Run the frontend and C# host together with live reload, including watched `carbon.d.ts` and capability sync |
| `carbon add nuget <package>` | Add any NuGet package to the C# backend |
| `carbon add plugin <name-or-package>` | Add a DotCarbon plugin, wire the backend, frontend package, and capabilities |
| `carbon build` | Compile a native, self-contained app into `out/` |
| `carbon icon` | Generate optimized Windows, macOS, and Linux icons |
| `carbon signer generate` | Generate an updater signing key pair |
| `carbon types` | Generate `carbon.d.ts` from your `[CarbonCommand]` methods and sync app command capabilities once |

Use any NuGet package in C#:

```bash
carbon add nuget SharpHook
```

Then call it from your own `[CarbonCommand]` methods. For frontend-facing DotCarbon plugins:

```bash
carbon add plugin Notification
```

Known first-party aliases wire NuGet, npm, `Program.cs`, and capabilities automatically. Third-party
DotCarbon plugins can be added by package id:

```bash
carbon add plugin Acme.DotCarbon.Foo \
  --class FooPlugin \
  --using Acme.DotCarbon.Foo \
  --namespace foo \
  --npm acme/dotcarbon-foo \
  '--command=foo:*'
```

Scoped npm packages can be written without the leading `@`; Carbon normalizes
`acme/dotcarbon-foo` to `@acme/dotcarbon-foo`.

## Configuration

Everything about your app lives in `carbon.json`, validated by the bundled schema:

```json
{
  "$schema": "./carbon.schema.json",
  "app": {
    "name": "my-app",
    "version": "0.1.0",
    "identifier": "com.example.my-app"
  },
  "window": {
    "title": "my-app",
    "width": 1200,
    "height": 800,
    "resizable": true,
    "center": true
  },
  "build": {
    "devCommand": "npm run dev",
    "buildCommand": "npm run build",
    "devUrl": "http://localhost:5173",
    "frontendDist": "ui/dist",
    "backendProject": "src-carbon"
  }
}
```

The `window` section supports size and position, `minWidth`/`minHeight`, `resizable`, `fullscreen`, `maximized`, `alwaysOnTop`, `decorations`, `transparent`, `devtools`, `contextMenu`, and `icon`.

## Building for production

```bash
carbon build
```

By default this produces one compressed, trimmed, self-contained executable with the frontend and `carbon.json` embedded. The operating system's webview is reused, so no browser engine ships with the app.

NativeAOT is available as an experimental size/startup option, but Photino's native library currently remains beside the executable:

```bash
carbon build --aot
```

Target another platform with `--target` (e.g. `--target win-x64`, `--target linux-x64`, `--target osx-arm64`).
Use `--target osx-universal --bundle` for one macOS application that runs natively on Apple Silicon and Intel Macs.

Installers and platform packages are opt-in so the normal output directory stays one file:

```bash
carbon build --bundle
```

The project template includes a 1024x1024 master icon at `src-carbon/icons/icon.png`.
Replace it, then run `carbon icon`; production builds also refresh generated icon formats automatically.

Platform signing, notarization, WebView2 deployment, resources, file associations, protocol handlers,
and signed updater artifacts are configured under `bundle`. See [Production distribution](DISTRIBUTION.md)
for the complete configuration and CI secret reference.

## Plugins

Native capabilities are opt-in — add the C# package, register the plugin, and call it from the frontend through its matching JS module.

| Capability | NuGet package | Frontend module |
|------------|---------------|-----------------|
| Clipboard | `DotCarbon.Plugins.Clipboard` | `@dotcarbon/plugin-clipboard` |
| Dialogs | `DotCarbon.Plugins.Dialog` | `@dotcarbon/plugin-dialog` |
| File system | `DotCarbon.Plugins.FileSystem` | `@dotcarbon/plugin-fs` |
| Global shortcuts | `DotCarbon.Plugins.GlobalShortcut` | `@dotcarbon/plugin-global-shortcut` |
| Deep links | `DotCarbon.Plugins.DeepLink` | `@dotcarbon/plugin-deep-link` |
| Notifications | `DotCarbon.Plugins.Notification` | `@dotcarbon/plugin-notification` |
| Opener | `DotCarbon.Plugins.Opener` | `@dotcarbon/plugin-opener` |
| Shell | `DotCarbon.Plugins.Shell` | `@dotcarbon/plugin-shell` |
| Single instance | `DotCarbon.Plugins.SingleInstance` | `@dotcarbon/plugin-single-instance` |
| Store | `DotCarbon.Plugins.Store` | `@dotcarbon/plugin-store` |
| Updater | `DotCarbon.Plugins.Updater` | `@dotcarbon/plugin-updater` |
| Window | `DotCarbon.Plugins.Window` | `@dotcarbon/plugin-window` |

`Shell` is intentionally scoped: `shell:execute` requires `plugins.shell.allowedPrograms`, working
directories are allowlisted with `allowedCwds`, URL schemes default to `http`, `https`, and `mailto`,
and opening local paths requires `allowOpenPaths: true`.

## Packages

| Package | Registry | Description |
|---------|----------|-------------|
| `@dotcarbon/create-app` | npm | Project scaffolder |
| `@dotcarbon/api` | npm | Frontend bridge SDK (`invoke`) |
| `DotCarbon.Cli` | NuGet | The `carbon` command-line tool (`dotnet tool`) |
| `DotCarbon.Core` | NuGet | Host runtime, configuration, and the command bridge |
| `DotCarbon.Plugins.*` | NuGet | Native capability plugins |

## Contributing

Contributions are welcome. This is early-stage software, so issues, ideas, and pull requests all help. Please open an issue to discuss significant changes before starting work.

## License

Released under the [MIT License](https://github.com/DanKaufmanDev/dotcarbon/blob/main/LICENSE).
