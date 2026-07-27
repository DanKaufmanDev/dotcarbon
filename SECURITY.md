# Security policy

## Reporting a vulnerability

Please report security vulnerabilities **privately**, not through public issues or pull requests.

Use [GitHub's private vulnerability reporting](https://github.com/DanKaufmanDev/dotcarbon/security/advisories/new)
(the repository's **Security → Report a vulnerability** tab). Include:

- the affected component (a plugin, the CLI, the bridge, a host) and version,
- a description and, where possible, a minimal reproduction,
- the impact you believe it has.

You'll get an acknowledgement, and we'll work on a fix and coordinate disclosure with you before it's
made public.

## Scope

DotCarbon's security model is the capability/ACL system: the bridge denies any command a window's
capabilities don't grant, remote content is denied the bridge by default, and plugins declare their
own permissions and scopes. Reports that show a way to **bypass those boundaries** — reach a command
without a granting capability, escape a plugin scope (fs paths, http origins, shell programs), or run
bridge calls from disallowed remote content — are especially valuable.

See the [security documentation](https://dotcarbon.dev/security/overview) for the model in full.

## Supported versions

DotCarbon is pre-1.0; fixes land on the latest published packages. Pin a version and watch releases
until the versioning policy takes effect at 1.0.
