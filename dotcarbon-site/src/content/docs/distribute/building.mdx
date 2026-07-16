---
title: Build for production
description: Publish a self-contained Carbon application or a native platform package.
---

Build the current desktop target without an installer:

```bash
carbon build
```

Carbon runs the frontend build, verifies `index.html`, embeds the frontend and effective manifest,
publishes the .NET host, removes symbols, and validates that the target output contains the expected
executable. Output is written to `out/<target>/`.

## Desktop targets

```bash
carbon build --target osx-arm64
carbon build --target osx-x64
carbon build --target win-x64
carbon build --target linux-x64
```

The regular build is trimmed, self-contained, and compressed. No separate .NET installation or Vite
server is required on the destination machine.

NativeAOT is available for smaller startup and runtime overhead:

```bash
carbon build --aot
```

NativeAOT currently retains the Photino native library as a sidecar. Libraries that require dynamic
code or unsupported reflection may need the regular self-contained build.

## Bundles

```bash
carbon bundle desktop
carbon bundle android --apk
carbon bundle android --aab
carbon bundle ios --simulator
carbon bundle ios --device
carbon bundle ios --archive
```

`carbon build --bundle` is the desktop-compatible alias for producing a native package. Use
`carbon bundle ... --dry-run` to inspect the planned work without changing output.

Every successful production build writes a manifest under `out/manifests/` with the artifact path,
type, size, SHA-256, application identity, icons, native integrations, and updater metadata.
