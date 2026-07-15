---
title: Architecture
description: How Carbon connects a Vite frontend, the .NET runtime, native hosts, and platform bundlers.
---

DotCarbon separates application behavior from the platform webview that displays it.

```text
Vite frontend
    │ @dotcarbon/api
    ▼
typed JSON bridge
    │ generated command bindings
    ▼
DotCarbon.Core ── application state, DI, events, capabilities, plugins
    │
    ├── DotCarbon.Host.Desktop ── Photino and native OS webview
    ├── DotCarbon.Host.Android ── Android WebView
    └── DotCarbon.Host.iOS ────── WKWebView
```

## Frontend

The frontend is a static Vite application. During development, the native webview loads
`build.devUrl`. Production builds embed the output directory into the .NET assembly and serve it
through `carbon://localhost`, including SPA fallback and the configured Content Security Policy.

## Command bridge

`[CarbonCommand]` methods are discovered by a Roslyn source generator. The generated registration
and serializer code avoids reflection-based invocation and remains compatible with trimming and
NativeAOT. The frontend sends a command name and payload; Carbon checks the calling window's
capabilities before dispatching it.

## Runtime

`CarbonApp` owns the service provider, managed state, plugin lifecycle, command registry, event bus,
and labeled windows. `AppHandle` is the stable handle passed to setup callbacks and plugins.

## Platform hosts

Hosts implement Carbon's webview contract and native message loop. Desktop supports multiple labeled
windows, each with one webview. Mobile currently presents one full-screen webview managed by the
Activity or UIApplication lifecycle.

## Bundler

The CLI builds the frontend once, embeds it into the host, and asks the platform toolchain for the
target artifact. Desktop targets can produce `.app`, `.dmg`, `.msi`, `.AppImage`, `.deb`, and `.rpm`.
Mobile targets produce APK/AAB and simulator/device/archive iOS builds.
