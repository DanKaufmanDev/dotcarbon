#!/usr/bin/env bash
# Installs the smoke APK on a booted emulator, launches it, and requires a full JS -> C# -> JS bridge
# round trip.
#
# This lives in a file rather than inline in the workflow because reactivecircus/android-emulator-runner
# executes its `script:` input ONE LINE PER `sh -c`: multi-line constructs (line continuations,
# if/else, loops) are syntax errors, and a variable assigned on one line is gone by the next. The
# workflow therefore calls this with a single line, and everything that needs real shell lives here.
set -euo pipefail

APK="${1:-${APK:-}}"
PACKAGE="${2:-dev.dotcarbon.mobilesmoke}"
TIMEOUT_SECONDS="${CARBON_SMOKE_TIMEOUT:-120}"

if [ -z "$APK" ]; then
    echo "::error::no APK path given (pass as \$1 or set APK)"
    exit 1
fi

echo "installing $APK"
adb install -r -t "$APK"
adb logcat -c

# `monkey` reports success but sometimes never starts the process (observed locally), which would show
# up as a mystery "no marker" failure. Resolve the launcher activity and start it directly, keeping
# monkey only as a fallback.
ACTIVITY=$(adb shell cmd package resolve-activity --brief "$PACKAGE" | tail -1 | tr -d '\r')
echo "launcher activity: ${ACTIVITY:-<unresolved>}"
if [ -n "$ACTIVITY" ]; then
    adb shell am start -n "$ACTIVITY"
else
    adb shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1
fi

# Assert the *round trip*, not just that C# ran: [[CARBON_WEB_READY]] is logged by the C# command
# (JS -> C# worked) and [[CARBON_BRIDGE_OK]] by the frontend once the reply lands (C# -> JS worked).
# Requiring only the first would miss a broken return path.
deadline=$((SECONDS + TIMEOUT_SECONDS))
until adb logcat -d | grep -q "\[\[CARBON_BRIDGE_OK\]\]"; do
    if [ "$SECONDS" -ge "$deadline" ]; then
        echo "::error::no [[CARBON_BRIDGE_OK]] marker in logcat after ${TIMEOUT_SECONDS}s"
        adb logcat -d | grep -aE "CARBON|Carbon" | tail -40 || true
        adb logcat -d | tail -100
        exit 1
    fi
    sleep 2
done

echo "bridge round-trip completed on the emulator"
adb logcat -d | grep -aE "CARBON_WEB_READY|CARBON_BRIDGE_OK"
