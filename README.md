# ⚡ DotCarbon

**Build cross-platform desktop apps with C# and web tech.** DotCarbon is a
Photino-based desktop app framework for .NET — a C#-native take on
[Tauri](https://tauri.app). Write your UI with any web framework, your backend
in C#, and ship a small native binary.

> **Status:** early / pre-release (`0.1.x`). APIs will change.

## Quick start

```bash
# Scaffold a new app (React, Vue, Svelte, Solid, Preact, or Vanilla)
npx @dotcarbon/create-app my-app --template react

cd my-app
carbon dev
```

`carbon dev` starts your frontend dev server and the .NET host together with
live reload. `carbon build` produces a self-contained app for distribution.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 18+ and a package manager (pnpm / npm / bun / yarn)
- The `carbon` CLI: `dotnet tool install -g DotCarbon.Cli`

## How it works

```
my-app/
├─ carbon.json        # app + window + build config
├─ src-carbon/        # the C# host (references DotCarbon.Core)
│  └─ Program.cs
└─ ui/                # your web frontend (Vite)
   └─ src/
```

The C# host loads your web UI in a native webview (via
[Photino](https://www.tryphotino.io)). Frontend and backend talk over a small
JSON bridge: the frontend sends a command, C# handlers marshal it to a plugin,
and the result comes back — the same request/response model as Tauri's `invoke`.

## Packages

| Package | Registry | What it is |
|---------|----------|------------|
| `@dotcarbon/create-app` | npm | Project scaffolder |
| `DotCarbon.Cli` | NuGet | The `carbon` CLI (`dotnet tool`) |
| `DotCarbon.Core` | NuGet | Host runtime, config, bridge |
| `DotCarbon.Plugins.*` | NuGet | Clipboard, Dialog, FileSystem, Notification, Shell, Window |

## CLI

| Command | Description |
|---------|-------------|
| `carbon dev` | Run frontend + .NET host in development with live reload |
| `carbon build` | Build the frontend and publish a self-contained app to `out/` |

## Repository layout

- `src/` — the .NET framework (Core, Cli, Host, Plugins.*)
- `dotcarbon-js/packages/create-app` — the npm scaffolder and its templates

## Contributing

Issues and PRs welcome. This is early-stage software — expect rough edges.

## License

[MIT](LICENSE)
