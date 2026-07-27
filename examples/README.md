# DotCarbon examples

Small, runnable apps that each demonstrate one capability. Clone the repo and run any of them:

```bash
carbon dev --project examples/tray-menu
# or bundle a native app:
carbon bundle desktop --project examples/sql
```

| Example | Shows |
| --- | --- |
| [`tray-menu`](tray-menu) | A system-tray icon and a native application menu that call C# |
| [`multiwindow`](multiwindow) | Opening a second labeled window from the frontend |
| [`sql`](sql) | A to-do list persisted in SQLite with the `sql` plugin |

## How these are wired

Each example references the framework by **project reference** (so it builds against this repo). In
your own app you'd reference the published packages instead — e.g.
`<PackageReference Include="DotCarbon.Host.Desktop" Version="..." />` — and `carbon add plugin <name>`
does that for you.

The UIs are plain static HTML with a tiny inlined `invoke()` bridge helper so they run with no
frontend build step. A real app imports the same `invoke` from `@dotcarbon/api` and can use any
framework (see `carbon init` / `@dotcarbon/create-app`).
