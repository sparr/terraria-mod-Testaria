#!/usr/bin/env bash
#
# Every gate, in the order that fails fastest.
#
#   1. core self-tests        no game needed, milliseconds
#   2. the green path         the self-test mod must pass in a live server
#   3. the red path           deliberate failures must be reported as such
#   4. packages              the artifacts must work when consumed as packages
#   5. tier 3                 a client must join, and must be missed when absent
#   6. fresh worlds           tests wanting an untouched world get one
#   7. calibration            the framework must work on someone else's mod
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

# Packaging is invisible to every unit test: they instantiate the analyzer and
# call into the tool directly, so all of them pass while a package ships
# without its analyzer or with MSBuild that will not load.
if [ "${SKIP_PACKAGES:-0}" = "1" ]; then
	echo
	echo "=== packages: skipped by request ==="
else
	step "packages" "$HERE/check-packages.sh"
fi

# A second process, a framebuffer, and a handshake. Slower than the gates
# above and faster than the two below.
if [ "${SKIP_NET:-0}" = "1" ]; then
	echo
	echo "=== tier 3: skipped by request ==="
else
	step "tier 3" "$HERE/check-net.sh"
fi

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
