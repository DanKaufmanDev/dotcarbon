---
title: Diagnostics
description: Validate configuration, plugins, capabilities, platforms, and signing.
---

Run the main diagnostic command from the project root:

```bash
carbon doctor
```

Doctor validates manifest shape and consistency, plugin platform support, security scopes, mobile
permissions, updater configuration, and native toolchain readiness. Warnings identify risky defaults;
errors produce a non-zero exit code for CI.

Check signing separately:

```bash
carbon doctor signing
```

Validate capability names and command patterns:

```bash
carbon capabilities check
```

Inspect generated platform drift:

```bash
carbon platform list
```

Preview a bundle plan without executing it:

```bash
carbon bundle desktop --dry-run
carbon bundle android --dry-run
carbon bundle ios --simulator --dry-run
```

When filing a build issue, include the target, Carbon version, .NET SDK and workload versions, active
Xcode or Android SDK version, and the first native toolchain error rather than only the final exit code.
