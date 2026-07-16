---
title: carbon.json reference
description: Complete reference for the DotCarbon application manifest.
---

## `app`

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | string | `Carbon App` | Internal and fallback display name |
| `displayName` | string? | `name` | Launcher and home-screen name |
| `version` | string | `0.1.0` | Semantic application version |
| `identifier` | string | `com.example.app` | Reverse-DNS application identity |

## `window` and `windows[]`

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | string | `main` | Unique runtime label |
| `url` | string? | frontend root | Initial relative or allowed URL |
| `parent` | string? | none | Parent window label |
| `capabilities` | string[] | `[]` | Assigned capability identifiers |
| `title` | string | `Carbon App` | Native window title |
| `width`, `height` | integer | `800`, `600` | Initial size |
| `minWidth`, `minHeight` | integer? | none | Minimum size |
| `maxWidth`, `maxHeight` | integer? | none | Maximum size |
| `x`, `y` | integer? | none | Initial position |
| `center` | boolean | `true` | Center after creation |
| `resizable` | boolean | `true` | Allow user resizing |
| `fullscreen` | boolean | `false` | Start fullscreen |
| `maximized` | boolean | `false` | Start maximized |
| `alwaysOnTop` | boolean | `false` | Keep above ordinary windows |
| `decorations` | boolean | `true` | Native frame and titlebar |
| `transparent` | boolean | `false` | Transparent webview/window background |
| `devTools` | boolean | `true` | Enable web inspector support |
| `contextMenu` | boolean | `true` | Enable default webview context menu |
| `icon` | string? | none | Source or generated window icon path |

## `build`

| Field | Default | Description |
| --- | --- | --- |
| `devCommand` | `pnpm dev` | Starts the frontend server |
| `buildCommand` | inferred | Builds production frontend assets |
| `devUrl` | `http://localhost:5173` | URL loaded by development webviews |
| `frontendDist` | project template value | Directory embedded in production |
| `backendProject` | auto-detected | Host project or directory |

## `security`

| Field | Default | Description |
| --- | --- | --- |
| `enabled` | `true` | Enforce window capabilities |
| `devAllowAll` | `false` | Bypass capability checks only on the dev origin |
| `allowExternalUrls` | `false` | Permit navigation outside app/dev origins |
| `allowSourceMaps` | `false` | Serve source map assets in production |
| `maxBridgeMessageBytes` | `1048576` | Maximum invoke message size |
| `maxEventPayloadBytes` | `262144` | Maximum event payload size |
| `contentSecurityPolicy` | restrictive default | CSP injected into production HTML |
| `allowedOrigins` | `[]` | Additional trusted frontend origins |
| `defaultCapabilities` | `[]` | Capabilities assigned to all windows |
| `capabilities` | `{}` | Inline capability definitions |

Inline capabilities contain `identifier`, `description`, `windows`, `commands`, and `permissions`.

## `plugins`

An object keyed by plugin namespace. Each plugin owns its value shape. See
[Plugin scopes](/security/plugin-scopes/) and individual plugin pages.

## `permissions`

Boolean device permissions are `camera`, `microphone`, `location`, `notifications`, `contacts`, and
`photoLibrary`. `files` accepts `appData`, `documents`, or `external`. `descriptions` maps permission
IDs to iOS usage strings.

## `bundle`

Common fields are `targets`, `publisher`, `copyright`, `category`, `resources`, `fileAssociations`,
and `protocols`.

`fileAssociations[]` contains `name`, `description`, `extensions`, `mimeType`, and `role`.
`protocols[]` contains `name` and `schemes`.

### `bundle.macOS`

`minimumSystemVersion`, `signingIdentity`, `entitlements`, `notarizationProfile`.

### `bundle.windows`

`webView2InstallMode`, `webView2InstallerPath`, `certificateThumbprint`, `timestampUrl`.
WebView2 modes are `downloadBootstrapper`, `offlineInstaller`, and `skip`.

### `bundle.linux`

`category`, `formats`, `maintainer`, `section`, `priority`, `depends`, `license`. Supported formats are
`appimage`, `deb`, and `rpm`.

### `bundle.android`

`package`, `minSdk`, `targetSdk`, `compileSdk`, and `signing` (`keystore`, `keyAlias`).

### `bundle.ios`

`bundleIdentifier`, `minimumOSVersion`, `developmentTeam`, and `signing` (`identity`,
`provisioningProfile`).

### `bundle.updater`

`active`, `createArtifacts`, `endpoints`, and `publicKey`.
