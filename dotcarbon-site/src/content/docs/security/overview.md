---
title: Security model
description: Understand Carbon's bridge, origin, capability, and plugin security boundaries.
---

Carbon treats frontend code as less privileged than the .NET backend. A webview can only reach C#
through registered bridge commands, and each call is checked against the capability set assigned to
the calling window.

## Defaults

- Capabilities are enabled and commands are denied until allowed.
- Production content is served from the private `carbon://localhost` origin.
- Remote origins do not receive implicit command access.
- Bridge payload size is bounded by configuration.
- Asset paths are normalized before resolving embedded content.
- Plugin scopes constrain sensitive resources such as files, URLs, processes, and working directories.
- Production configuration and capability files are embedded into the app.

## Responsibility boundaries

Capabilities answer **which command may run**. Plugin scopes answer **which resources that command may
touch**. Both checks matter: granting `fs:read_file` should not give the window unrestricted access to
the entire filesystem.

Keep command DTOs narrow, validate domain values in C#, and avoid generic commands that accept an
arbitrary method name, SQL statement, script, or shell line.

## Development escape hatches

`security.devAllowAll` bypasses capability checks only while the frontend is loaded from the configured
development server. Packaged applications still enforce capabilities. `security.enabled: false` opens
the bridge completely and should be reserved for isolated experiments.

Run `carbon doctor` before release to identify broad or missing plugin scopes, capability problems,
invalid origins, and signing configuration issues.
