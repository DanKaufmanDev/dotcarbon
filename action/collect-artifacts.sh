#!/usr/bin/env bash
# Collects what `carbon bundle` produced and stages it for a GitHub release.
#
# The logic lives in a script rather than inline YAML so it can be run — and therefore verified —
# locally against real bundle output, instead of only ever executing on a runner.
#
# Usage: collect-artifacts.sh <project-dir> <staging-dir> [include-updater-json]
# Writes the staged files to <staging-dir> and prints one path per line.
set -euo pipefail

PROJECT="${1:-.}"
STAGING="${2:?staging directory required}"
INCLUDE_UPDATER="${3:-true}"

OUT="$PROJECT/out"
if [ ! -d "$OUT" ]; then
    # Single quotes around the command name: backticks inside a double-quoted string would be
    # command substitution, and would actually run it.
    echo "::error::no out/ directory in $PROJECT — did 'carbon bundle' run?" >&2
    exit 1
fi

mkdir -p "$STAGING"

# Installer/package formats Carbon produces, plus the detached updater signature that pairs with them.
# `out/manifests/*.build.json` is build metadata, not a release asset, so it is deliberately excluded.
#
# `find` rather than `**` on purpose: macOS runners ship bash 3.2, which has no globstar, so a `**`
# glob would silently match nothing there.
count=0
while IFS= read -r path; do
    cp "$path" "$STAGING/"
    count=$((count + 1))
done < <(find "$OUT" -type f \( \
    -name '*.dmg' -o -name '*.msi' -o -name '*.exe' -o -name '*.AppImage' \
    -o -name '*.deb' -o -name '*.rpm' -o -name '*.zip' -o -name '*.sig' \) | sort)

if [ "$count" -eq 0 ]; then
    echo "::error::carbon bundle produced no installer artifacts under $OUT" >&2
    exit 1
fi

# Updater manifests: the client fetches a per-target manifest, and bundle.updater.endpoints templates
# on {{target}} — so `osx-arm64.update.json` has to be published as `osx-arm64.json` for an endpoint
# like https://…/releases/latest/download/{{target}}.json to resolve.
if [ "$INCLUDE_UPDATER" = "true" ]; then
    while IFS= read -r manifest; do
        target=$(basename "$(dirname "$manifest")")
        cp "$manifest" "$STAGING/$target.json"
    done < <(find "$OUT" -type f -name '*.update.json' | sort)
fi

# One path per line, for the caller to upload.
find "$STAGING" -type f | sort
