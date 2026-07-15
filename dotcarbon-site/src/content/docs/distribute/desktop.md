---
title: Desktop packages
description: Produce native macOS, Windows, and Linux packages.
---

## macOS

```bash
carbon bundle desktop --target osx-arm64
carbon bundle desktop --target osx-x64
carbon bundle desktop --target osx-universal
```

Carbon creates an `.app` and `.dmg`. Universal packages use a native universal launcher with
architecture-specific self-contained .NET payloads, avoiding changes to appended single-file data.

## Windows

Install WiX 4, then bundle:

```powershell
dotnet tool install --global wix --version 4.*
carbon bundle desktop --target win-x64
```

The `.msi` can bootstrap WebView2, carry an offline installer, or skip WebView2 setup according to
`bundle.windows.webView2InstallMode`.

## Linux

```bash
carbon bundle desktop --target linux-x64
```

Configure one or more formats:

```json
{
  "bundle": {
    "linux": {
      "formats": ["appimage", "deb", "rpm"],
      "maintainer": "Example <dev@example.com>",
      "depends": ["libwebkit2gtk-4.1-0"]
    }
  }
}
```

Carbon writes desktop entries, hicolor icons, categories, MIME associations, and protocol handlers
into Linux packages. Package dependencies remain distribution-specific and should be tested on each
supported distribution.

## Resources and native integrations

`bundle.resources` copies additional files into the platform resource location. File associations and
URL protocols are translated to Info.plist, MSI registry entries, and Linux desktop/MIME metadata.
