#!/usr/bin/env bash
#
# Every gate, in the order that fails fastest.
#
#   1. core self-tests        no game needed, milliseconds
#   2. the green path         the self-test mod must pass in a live server
#   3. the red path           deliberate failures must be reported as such
#   4. packages              the artifacts must work when consumed as packages
#   5. tier 3                 a client must join, and must be missed when absent
#   5a. load                  the arena under pressure, only when asked for
#   6. fresh worlds           tests wanting an untouched world get one
#   7. calibration            the framework must work on someone else's mod
#
# Green alone proves little: a framework that cannot report failure looks
# exactly like one that works, which is why 3 is not optional.
#
# Two environment variables turn this from a developer's loop into the body of
# a CI run. scripts/ci-local.sh sets both.
#
#   RESULTS_DIR   Keep what each gate produced: one log per gate, the JUnit
#                 report from every gate that writes one, and gates.tsv naming
#                 each gate with its verdict and how long it took. Unset,
#                 nothing is written down and everything goes to the terminal.
#
#   STRICT=1      A skipped gate is a failed gate. The SKIP_* variables exist
#                 so a developer can cut a slow gate out of a quick loop; a CI
#                 run that honored them would report success for a suite it
#                 never ran, and a missing ExampleMod would quietly shrink the
#                 run from eight gates to seven.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"
failures=0

STRICT="${STRICT:-0}"
RESULTS_DIR="${RESULTS_DIR:-}"
if [ -n "$RESULTS_DIR" ]; then
	mkdir -p "$RESULTS_DIR" || exit 2
	# Absolute, because the gates below run from directories of their own.
	RESULTS_DIR="$(cd "$RESULTS_DIR" && pwd)"
	export RESULTS_DIR
	printf 'gate\tverdict\tseconds\n' > "$RESULTS_DIR/gates.tsv"
fi

# A file name for a gate, derived from the name it is announced by.
slug() {
	echo "$1" | tr '[:upper:] ' '[:lower:]-' | tr -cd 'a-z0-9-'
}

record() {
	[ -n "$RESULTS_DIR" ] || return 0
	printf '%s\t%s\t%s\n' "$1" "$2" "$3" >> "$RESULTS_DIR/gates.tsv"
}

step() {
	local name="$1"; shift
	local started=$SECONDS rc=0
	echo
	echo "=== $name ==="
	if [ -n "$RESULTS_DIR" ]; then
		# tee rather than a redirect, so a watching human sees a long gate
		# make progress instead of going silent for an hour. pipefail is set
		# above, so the pipeline's status is the gate's own rather than tee's.
		"$@" 2>&1 | tee "$RESULTS_DIR/$(slug "$name").log"
		rc=$?
	else
		"$@"
		rc=$?
	fi
	if [ "$rc" -eq 0 ]; then
		echo "--- $name: ok"
		record "$name" ok "$((SECONDS - started))"
	else
		echo "--- $name: FAILED" >&2
		record "$name" failed "$((SECONDS - started))"
		failures=$((failures + 1))
	fi
}

# A gate that is not going to run. In strict mode that is a failure, because
# the alternative is a run reporting success for work it never did.
skip() {
	local name="$1" reason="$2"
	echo
	if [ "$STRICT" = "1" ]; then
		echo "=== $name: NOT RUN ($reason) ===" >&2
		echo "--- $name: FAILED, strict mode does not skip gates" >&2
		record "$name" not-run 0
		failures=$((failures + 1))
	else
		echo "=== $name: skipped ($reason) ==="
		record "$name" skipped 0
	fi
}

# Where a gate's JUnit report should land, if anywhere. Named per gate, so two
# gates driving the same runner do not overwrite each other's report.
results_out() {
	[ -n "$RESULTS_DIR" ] && echo "$RESULTS_DIR/$1.xml"
}

step "core self-tests" dotnet test "$ROOT" --nologo -v q

step "green path" env BLANK=1 RESULTS_OUT="$(results_out green-path)" "$HERE/run-tests.sh"

step "red path" "$HERE/check-red.sh"

# Packaging is invisible to every unit test: they instantiate the analyzer and
# call into the tool directly, so all of them pass while a package ships
# without its analyzer or with MSBuild that will not load.
if [ "${SKIP_PACKAGES:-0}" = "1" ]; then
	skip "packages" "by request"
	skip "templates" "by request"
else
	step "packages" "$HERE/check-packages.sh"
	step "templates" "$HERE/check-templates.sh"
fi

# A second process, a framebuffer, and a handshake. Slower than the gates
# above and faster than the two below.
if [ "${SKIP_NET:-0}" = "1" ]; then
	skip "tier 3" "by request"
else
	step "tier 3" "$HERE/check-net.sh"
fi

# Deliberate rather than automatic, as PLAN.md section 8.4 asks: a minute of
# work aimed at one component, worth running when the arena changes. It is also
# the one gate that measures rather than asserts, so a machine under load makes
# it report a slower arena rather than a broken one. Strict mode does not
# demand it for that reason.
if [ "${RUN_LOAD:-0}" = "1" ]; then
	step "load" "$HERE/check-load.sh"
else
	echo
	echo "=== load: not requested (RUN_LOAD=1 runs it) ==="
	record "load" not-requested 0
fi

# Slow by construction: a server start per test. Skippable for a quick loop.
if [ "${SKIP_FRESH:-0}" = "1" ]; then
	skip "fresh worlds" "by request"
else
	# MIN_TESTS deliberately cleared: each fresh-world process runs exactly one
	# test, so a demand meant for a whole suite would fail every one of them.
	step "fresh worlds" env MIN_TESTS= "$HERE/run-fresh.sh"
fi

if [ "${SKIP_CALIBRATION:-0}" = "1" ]; then
	skip "calibration against ExampleMod" "by request"
elif [ -f "$MODS_SRC/ExampleMod.tmod" ]; then
	step "calibration against ExampleMod" env \
		ENABLED="Testaria ExampleMod TestariaExampleTest" \
		RUN_NAME="ExampleModSuite" \
		BLANK=1 RESULTS_OUT="$(results_out calibration-against-examplemod)" "$HERE/run-tests.sh"
else
	skip "calibration against ExampleMod" "no ExampleMod.tmod, run scripts/build-examplemod.sh"
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "all gates passed"
else
	echo "$failures gate(s) failed" >&2
fi
[ -n "$RESULTS_DIR" ] && echo "results in $RESULTS_DIR"
exit "$failures"
