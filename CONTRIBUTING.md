# Contributing to DotCarbon

Thanks for helping build DotCarbon. This guide covers the local setup, the layout of the repo, and
the conventions a pull request is expected to follow.

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10+ | The framework, CLI, and tests |
| [Node.js](https://nodejs.org) | 18+ | The `@dotcarbon/*` packages and the docs site |
| Xcode | latest | macOS/iOS bundling and the iOS simulator (macOS only) |
| Android SDK + a JDK | API 34+ | Android bundling and the emulator |

Mobile toolchains are only needed when you touch the mobile hosts or plugins; desktop work needs just
.NET and Node.

## Build and test

The solution is `DotCarbon.slnx`.

```bash
dotnet build DotCarbon.slnx
dotnet test DotCarbon.slnx
```

Before opening a PR, run the same gate CI runs — a Release build with warnings treated as errors,
which catches the trim/AOT diagnostics a Debug build misses:

```bash
dotnet build DotCarbon.slnx -c Release -p:TreatWarningsAsErrors=true -p:NuGetAudit=false
```

For the JavaScript packages:

```bash
cd dotcarbon-js && pnpm install && pnpm -r build
```

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/DotCarbon.Core` | Runtime: host, config, and the JS↔C# bridge (Photino-free) |
| `src/DotCarbon.Host.Desktop` / `.Android` / `.iOS` | Per-platform hosts behind `ICarbonWebView` |
| `src/DotCarbon.Plugins.*` | First-party plugins (`.Native` siblings hold the mobile bindings) |
| `src/DotCarbon.Cli` | The `carbon` command-line tool |
| `src/DotCarbon.Generators` | The source generator that makes the bridge reflection-free |
| `dotcarbon-js/packages/*` | `@dotcarbon/api`, `@dotcarbon/plugin-*`, and `create-app` |
| `dotcarbon-site` | The documentation site (Astro Starlight) |
| `tests/DotCarbon.Tests` | The test suite |

## Writing a plugin

Scaffold one rather than copying by hand — the template encodes the parts that fail late otherwise
(the `partial` class the generator needs, the `[CarbonPermission]`, the `[JsonSerializable]` entries):

```bash
carbon plugin new my-plugin
```

See [Authoring a plugin](https://dotcarbon.dev/plugins/authoring) for the full walkthrough. A
first-party plugin also needs a catalog entry in `AddCommand.cs` and, if it maps to a device
permission, an entry in `PermissionCatalog.cs` **and** `carbon.schema.json`.

## The API reference is generated

`dotcarbon-site/src/content/docs/reference/commands.md` is produced from the plugin sources — do not
edit it by hand. If you add or change a command, regenerate it (CI fails otherwise):

```bash
carbon docs --project src --output dotcarbon-site/src/content/docs/reference/commands.md
```

## Commit and pull-request conventions

- **Commit messages** follow [Conventional Commits](https://www.conventionalcommits.org):
  `feat(updater): download progress events`, `fix(ci): …`, `docs: …`, `refactor: …`, `test: …`,
  `chore: …`. The scope is the area touched (`cli`, `bundle`, a plugin name, …).
- **One logical change per PR.** Keep unrelated cleanups out of a feature PR.
- **Every change ships with tests.** Prefer a test that fails without your change. Platform-specific
  behavior that can't run in CI (real signing, device installs) should be verified manually and the
  verification described in the PR.
- **Update the docs** in `dotcarbon-site` when you change user-facing behavior, and regenerate the
  command reference if you touched a command surface.
- Label your PR so it lands in the right release-notes section (see `.github/release.yml`): `feature`,
  `fix`, `docs`, `performance`, `security`, `dependencies`, or `chore`.

## CI

`.github/workflows/ci.yml` runs the tests, the Release/warnaserror build, publish-output and
desktop-package smokes across macOS/Windows/Linux, and the API-reference drift check.
`mobile-smoke.yml` boots the app on an Android emulator and an iOS simulator and asserts a real
JS→C#→JS bridge round trip. A PR is expected to be green before review.

## Reporting bugs and requesting features

Open an issue using the templates under [`.github/ISSUE_TEMPLATE`](.github/ISSUE_TEMPLATE). For a bug,
`carbon info` output is the fastest way to tell us your exact toolchain. Security issues go through
[SECURITY.md](SECURITY.md), not public issues.
