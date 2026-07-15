---
title: CLI reference
description: Commands and options provided by the Carbon .NET tool.
---

Install or update:

```bash
dotnet tool install --global DotCarbon.Cli
dotnet tool update --global DotCarbon.Cli
```

## Development and generation

| Command | Purpose |
| --- | --- |
| `carbon dev [desktop]` | Run Vite and the watched desktop host |
| `carbon dev android` | Build and deploy to Android |
| `carbon dev ios` | Build and run on a booted iOS simulator |
| `carbon types` | Generate `carbon.d.ts` and sync app capabilities |
| `carbon icon` | Generate desktop and mobile icon assets |

Common options include `--project <directory>`. `carbon types` accepts `--out` and
`--no-capabilities`; `carbon icon` accepts `--input` and `--output`.

## Dependencies

```bash
carbon add nuget <package> [--version <version>]
carbon add plugin <alias-or-package> [plugin metadata options]
```

Known plugin aliases wire backend, frontend, registration, and permissions. Third-party plugin options
include `--class`, `--using`, `--namespace`, `--npm`, and repeated `--command` values.

## Platforms

```bash
carbon platform add <desktop|android|ios>
carbon platform sync <desktop|android|ios> [--force]
carbon platform list
```

## Capabilities

```bash
carbon capabilities list
carbon capabilities add <permission> [--capability main] [--window main]
carbon capabilities sync
carbon capabilities check
```

`carbon capability` is an alias for `carbon capabilities`.

## Desktop production

```bash
carbon build [--target <rid>] [--aot] [--bundle] [--updater-artifacts]
carbon bundle desktop [--target <rid>] [--aot] [--no-package]
                      [--updater-artifacts] [--dry-run] [--verify]
```

Without a subcommand, `carbon bundle` bundles the current desktop target.

## Android production

```bash
carbon bundle android [--apk | --aab] [--debug | --release]
                      [--dry-run] [--allow-unsupported-plugins]
```

APK and Release are the defaults.

## iOS production

```bash
carbon bundle ios [--simulator | --device | --archive]
                  [--dry-run] [--allow-unsupported-plugins]
```

Simulator is the default.

## Diagnostics and signing

```bash
carbon doctor
carbon doctor signing
carbon signer generate --output <private-key-path>
```

Use `carbon <command> --help` for the exact options supported by the installed version.
