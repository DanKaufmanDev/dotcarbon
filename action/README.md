# Carbon build action

Builds a DotCarbon app on each platform, collects the installers, and (optionally) publishes them —
with signed updater manifests — to a GitHub release. The Carbon equivalent of `tauri-action`.

```yaml
name: Release
on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  release:
    strategy:
      fail-fast: false
      matrix:
        os: [macos-latest, windows-latest, ubuntu-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: actions/setup-node@v4
        with:
          node-version: 22

      - uses: DanKaufmanDev/dotcarbon/action@main
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          # Signing and updater secrets — see docs/SIGNING.md.
          APPLE_SIGNING_IDENTITY: ${{ secrets.APPLE_SIGNING_IDENTITY }}
          APPLE_NOTARIZATION_PROFILE: ${{ secrets.APPLE_NOTARIZATION_PROFILE }}
          CARBON_UPDATER_PRIVATE_KEY: ${{ secrets.CARBON_UPDATER_PRIVATE_KEY }}
        with:
          tagName: ${{ github.ref_name }}
          releaseName: ${{ github.ref_name }}
```

Every job in the matrix uploads into the **same** release: the first to run creates it, the rest
upload into it, so there is no race to create and no duplicate releases.

## Inputs

| Input | Default | Description |
| --- | --- | --- |
| `projectPath` | `.` | Directory containing `carbon.json`. |
| `args` | | Extra arguments appended to `carbon bundle desktop`. |
| `carbonVersion` | latest | Version of the `DotCarbon.Cli` tool to install. |
| `installFrontendDeps` | `true` | Install frontend dependencies first, using the project's lockfile. |
| `tagName` | | Release tag to upload to. **Empty means build only** — nothing is published. |
| `releaseName` | | Release title (defaults to the tag). |
| `releaseBody` | | Release notes. |
| `releaseDraft` | `false` | Create the release as a draft. |
| `prerelease` | `false` | Mark the release as a prerelease. |
| `includeUpdaterJson` | `true` | Publish each signed updater manifest as `<target>.json`. |

## Outputs

| Output | Description |
| --- | --- |
| `artifacts` | Newline-separated paths of the staged artifacts. |
| `stagingPath` | Directory holding them. |

## Updater manifests

When `bundle.updater.createArtifacts` is on and `CARBON_UPDATER_PRIVATE_KEY` is set, `carbon bundle`
emits `<artifact>.sig` and `<artifact>.update.json` per target. This action publishes each manifest as
`<target>.json`, which is what a templated endpoint resolves to:

```json
{
  "bundle": {
    "updater": {
      "active": true,
      "createArtifacts": true,
      "endpoints": ["https://github.com/OWNER/REPO/releases/latest/download/{{target}}.json"]
    }
  }
}
```

`{{target}}` expands to the runtime identifier (`osx-arm64`, `win-x64`, `linux-x64`), so each platform
fetches its own manifest. Set `includeUpdaterJson: false` to skip publishing them.

## What gets uploaded

Installer formats (`.dmg`, `.msi`, `.exe`, `.AppImage`, `.deb`, `.rpm`, `.zip`), each `.sig`, and the
renamed updater manifests. Build metadata under `out/manifests/` is deliberately **not** uploaded — it
describes the build, it is not a release asset.

## Publishing

Uploading needs `GITHUB_TOKEN` in the step's `env` and `permissions: contents: write` on the job. The
action fails with a clear message if the token is missing rather than silently skipping the upload.
