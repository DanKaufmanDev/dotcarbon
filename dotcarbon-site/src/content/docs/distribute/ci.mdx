---
title: Continuous integration
description: Build, test, package, and publish Carbon apps in a platform matrix.
---

Native packages should be built on their target operating systems. A practical GitHub Actions matrix is:

| Runner | Typical outputs |
| --- | --- |
| `macos-15` | arm64/x64/universal `.app` and `.dmg`, iOS simulator/archive |
| `windows-latest` | win-x64 executable and `.msi` |
| `ubuntu-latest` | linux-x64 `.AppImage`, `.deb`, `.rpm`, Android APK/AAB |

Each job should:

1. Check out the same commit.
2. Install the pinned .NET SDK and required workload.
3. Install Node and the project's package manager.
4. Restore with the committed lockfile.
5. Run unit tests and `carbon capabilities check`.
6. Build or bundle the platform artifact.
7. Verify the output and upload it without recompression when signatures cover exact bytes.

Keep signing identities, certificates, passwords, and updater private keys in protected environment
secrets. Use release environments for production credentials and require approval where appropriate.

For mobile smoke tests, boot an emulator/simulator, install the produced artifact, launch it, and
assert a real JavaScript-to-C# bridge round trip. Build-only jobs do not catch missing native
executables, fast-deployment APKs, capability omissions, or WebView startup failures.
