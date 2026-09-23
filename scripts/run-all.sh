#!/usr/bin/env bash
#
# Every gate, in the order that fails fastest.
#
#   1. core self-tests        no game needed, milliseconds
#   2. the green path         the self-test mod must pass in a live server
#   3. the red path           deliberate failures must be reported as such
#   4. fresh worlds           tests wanting an untouched world get one
#   5. calibration            the framework must work on someone else's mod
#
# Green alone proves little: a framework that cannot report failure looks
# exactly like one that works, which is why 3 is not optional.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"
failures=0

step() {
	local name="$1"; shift
	echo
	echo "=== $name ==="
	if "$@"; then
		echo "--- $name: ok"
	else
		echo "--- $name: FAILED" >&2
		failures=$((failures + 1))
	fi
}

step "core self-tests" dotnet test "$ROOT" --nologo -v q

step "green path" env BLANK=1 "$HERE/run-tests.sh"

step "red path" "$HERE/check-red.sh"

# Slow by construction: a server start per test. Skippable for a quick loop.
if [ "${SKIP_FRESH:-0}" = "1" ]; then
	echo
	echo "=== fresh worlds: skipped by request ==="
else
	step "fresh worlds" "$HERE/run-fresh.sh"
fi

if [ "${SKIP_CALIBRATION:-0}" = "1" ]; then
	echo
	echo "=== calibration: skipped by request ==="
elif [ -f "$MODS_SRC/ExampleMod.tmod" ]; then
	step "calibration against ExampleMod" env \
		ENABLED="Testaria ExampleMod TestariaExampleTest" \
		RUN_NAME="ExampleModSuite" \
		BLANK=1 "$HERE/run-tests.sh"
else
	echo
	echo "=== calibration: skipped, no ExampleMod.tmod (run scripts/build-examplemod.sh) ==="
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "all gates passed"
else
	echo "$failures gate(s) failed" >&2
fi
exit "$failures"
