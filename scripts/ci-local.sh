#!/usr/bin/env bash
#
# Continuous integration for the game tiers, on this machine.
#
# PLAN.md section 8.7 ships a GitHub job for tiers 1 through 3, and section
# 8.3a spells out what that job has to do: obtain Terraria, derive an ownership
# key, decompile and build tModLoader, then run the gates. Every one of those
# steps exists to reconstruct, on a rented machine, what a developer's own
# machine already has. So the gates can be run under CI discipline here long
# before any of that, and the recipe that ships in 8.7 is then a port of
# something that has been running rather than a first attempt.
#
# What this adds over running scripts/run-all.sh by hand:
#
#   - a preflight that names what is missing instead of failing inside a gate
#   - provisioning, so the calibration subject is current rather than whatever
#     .tmod was built last
#   - strict mode, so a gate that cannot run is a failure rather than a line
#     of output nobody reads
#   - a run directory per run: every gate's log, every JUnit report, and a
#     summary, kept after the terminal is gone
#   - a lock, because two runs share one save directory and one port
#   - a record of which commit produced which verdict, which is what --if-new
#     reads to decide whether there is anything to do
#
# Usage:
#
#   scripts/ci-local.sh              run the gates against the working tree
#   scripts/ci-local.sh --if-new     run only for a commit not yet recorded
#   scripts/ci-local.sh --load       include the arena load gate
#
# --if-new is the one a timer wants. scripts/systemd/ has the units.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"

IF_NEW=0
FORCE=0
WITH_LOAD=0
KEEP=20
RUNS_DIR="${TESTARIA_CI_RUNS:-$ROOT/ci-runs}"

usage() {
	sed -n '3,/^set -/p' "$0" | sed 's/^# \{0,1\}//; s/^#$//'
	exit "${1:-0}"
}

while [ "$#" -gt 0 ]; do
	case "$1" in
		--if-new) IF_NEW=1 ;;
		--force) FORCE=1 ;;
		--load) WITH_LOAD=1 ;;
		--keep) KEEP="${2:?--keep needs a number}"; shift ;;
		--runs-dir) RUNS_DIR="${2:?--runs-dir needs a path}"; shift ;;
		-h|--help) usage 0 ;;
		*) echo "unknown argument: $1 (try --help)" >&2; exit 2 ;;
	esac
	shift
done

# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

say() { echo "[ci] $*"; }
die() { echo "[ci] $*" >&2; exit 2; }

# ---------------------------------------------------------------------------
# Preflight
#
# Every one of these is something a gate depends on and none of them announces
# itself well when absent: a missing Xvfb surfaces as a client that joins and
# then goes quiet, and a missing systemd-run as a server that never starts. A
# check here costs a second and names the fix.
# ---------------------------------------------------------------------------

missing=0
need() {
	command -v "$1" >/dev/null || { echo "[ci] missing: $1 ($2)" >&2; missing=1; }
}

need git "needed to record which commit a run covered"
need dotnet "the .NET SDK, https://dotnet.microsoft.com/download"
need python3 "the gates parse their JUnit reports with it"
need rsync "scripts/build-examplemod.sh copies the calibration sources with it"
need Xvfb "a tier 3 client needs a real display; package 'xorg-server-xvfb' or 'xvfb'"
need systemd-run "the gates cap the game's memory with a transient scope"
need flock "one run at a time, and this is what holds the lock"

[ -f "$TML_PATH/tModLoader.dll" ] \
	|| { echo "[ci] no tModLoader at $TML_PATH (set TML_PATH, or run scripts/discover-paths.sh)" >&2; missing=1; }
[ -f "$TML_PATH/tMLMod.targets" ] \
	|| { echo "[ci] no tMLMod.targets at $TML_PATH, so no mod can be built" >&2; missing=1; }
[ -d "$MODS_SRC" ] \
	|| { echo "[ci] no Mods directory at $MODS_SRC (set MODS_SRC, or run the game once)" >&2; missing=1; }
# The calibration gate is not optional here. Strict mode would fail on a
# missing ExampleMod anyway; failing now says why.
[ -n "${EXAMPLEMOD_SRC:-}" ] && [ -d "$EXAMPLEMOD_SRC" ] \
	|| { echo "[ci] no ExampleMod sources (set EXAMPLEMOD_SRC to the ExampleMod directory in a tModLoader checkout)" >&2; missing=1; }

[ "$missing" -eq 0 ] || die "preflight failed; nothing was run"

# ---------------------------------------------------------------------------
# One run at a time
#
# Concurrent runs would share the save directory the mods are built into and
# the port the server listens on, so the second one fails in ways that look
# like the framework's fault. A timer that fires while a run is still going
# declines quietly; anything else says so and stops.
# ---------------------------------------------------------------------------

mkdir -p "$RUNS_DIR" || die "cannot create $RUNS_DIR"
RUNS_DIR="$(cd "$RUNS_DIR" && pwd)"
LOCK="$RUNS_DIR/.lock"

exec 9>"$LOCK" || die "cannot open $LOCK"
if ! flock -n 9; then
	if [ "$IF_NEW" = "1" ]; then
		say "another run holds the lock; nothing to do"
		exit 0
	fi
	die "another run holds $LOCK"
fi

# ---------------------------------------------------------------------------
# What is being tested
# ---------------------------------------------------------------------------

COMMIT="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null)" || die "not a git checkout: $ROOT"
SHORT="$(git -C "$ROOT" rev-parse --short HEAD)"
BRANCH="$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)"
DIRTY=0
[ -n "$(git -C "$ROOT" status --porcelain)" ] && DIRTY=1

HISTORY="$RUNS_DIR/history.tsv"
[ -f "$HISTORY" ] || printf 'finished\tcommit\tdirty\tverdict\tseconds\trun\n' > "$HISTORY"

if [ "$IF_NEW" = "1" ] && [ "$FORCE" = "0" ]; then
	# A dirty tree is not any commit, so a scheduled run has nothing it can
	# honestly record. It declines rather than testing a moving target every
	# time it fires.
	if [ "$DIRTY" = "1" ]; then
		say "working tree is dirty; a scheduled run tests commits, not edits"
		exit 0
	fi
	if awk -F'\t' -v c="$COMMIT" '$2 == c && $3 == "clean" { found = 1 } END { exit !found }' "$HISTORY"; then
		say "$SHORT is already recorded; nothing to do"
		exit 0
	fi
fi

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RUN="$RUNS_DIR/$STAMP-$SHORT"
mkdir -p "$RUN" || die "cannot create $RUN"
LOG="$RUN/run.log"

{
	echo "commit:   $COMMIT"
	echo "short:    $SHORT"
	echo "branch:   $BRANCH"
	echo "tree:     $([ "$DIRTY" = "1" ] && echo dirty || echo clean)"
	echo "started:  $STAMP"
	echo "checkout: $ROOT"
	echo "tml:      $TML_PATH"
	echo "mods:     $MODS_SRC"
	echo "host:     $(uname -sr) $(hostname)"
	echo "dotnet:   $(dotnet --version 2>/dev/null)"
	echo "load:     $(cut -d' ' -f1-3 /proc/loadavg 2>/dev/null)"
} > "$RUN/run.meta"

say "run $STAMP-$SHORT on $BRANCH$([ "$DIRTY" = "1" ] && echo ' (dirty)')"
say "results in $RUN"

started=$SECONDS

# ---------------------------------------------------------------------------
# Provisioning
#
# ExampleMod is the calibration subject and lives in a tModLoader checkout
# rather than in this repository, so a run that used whatever .tmod happened to
# be lying around would be calibrating against an unknown version of it. The
# build is incremental, so this is cheap after the first time.
# ---------------------------------------------------------------------------

say "building the calibration subject"
if ! nice -n 19 "$HERE/build-examplemod.sh" > "$RUN/provision.log" 2>&1; then
	say "could not build ExampleMod; see $RUN/provision.log"
	tail -20 "$RUN/provision.log" >&2
	printf '%s\t%s\t%s\t%s\t%s\t%s\n' \
		"$(date -u +%Y%m%dT%H%M%SZ)" "$COMMIT" \
		"$([ "$DIRTY" = "1" ] && echo dirty || echo clean)" \
		"provision-failed" "$((SECONDS - started))" "$RUN" >> "$HISTORY"
	exit 2
fi

# ---------------------------------------------------------------------------
# The gates
# ---------------------------------------------------------------------------

say "running the gates"
STRICT=1 RESULTS_DIR="$RUN" RUN_LOAD="$WITH_LOAD" \
	nice -n 19 "$HERE/run-all.sh" 2>&1 | tee "$LOG"
rc=$?
elapsed=$((SECONDS - started))

# ---------------------------------------------------------------------------
# The summary
#
# gates.tsv says which gates passed; the JUnit reports say how much each one
# actually proved. A gate that passes having run nothing is the failure mode
# MIN_TESTS exists for, and a summary that only counted gates would hide it.
# ---------------------------------------------------------------------------

python3 - "$RUN" "$elapsed" "$rc" > "$RUN/summary.txt" 2>&1 <<'PY'
import sys, glob, os, xml.etree.ElementTree as ET

run, elapsed, rc = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])

gates = []
path = os.path.join(run, "gates.tsv")
if os.path.exists(path):
    with open(path) as handle:
        next(handle, None)
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 3:
                gates.append(parts)

width = max([len(name) for name, _, _ in gates] + [4])
print("gates")
for name, verdict, seconds in gates:
    print(f"  {name:<{width}}  {verdict:<13} {seconds:>5}s")

print()
print("reports")
total = failed = errored = skipped = 0
for report in sorted(glob.glob(os.path.join(run, "*.xml"))):
    name = os.path.basename(report)[:-4]
    try:
        root = ET.parse(report).getroot()
    except ET.ParseError as bad:
        print(f"  {name:<26} unreadable: {bad}")
        continue
    tests = int(root.get("tests", 0))
    fails = int(root.get("failures", 0))
    errs = int(root.get("errors", 0))
    skips = int(root.get("skipped", 0))
    # The red path's failures are its purpose: check-red.sh passes only when
    # all six arrive, each as the right kind of problem. Counting them with
    # the rest would put "2 failed, 4 errored" under a verdict of pass and
    # make a green run read as a broken one.
    if name == "red-path":
        print(f"  {name:<26} {tests:>5} tests, {fails} failed and {errs} errored "
              f"on purpose, which is the gate")
        continue
    total += tests
    failed += fails
    errored += errs
    skipped += skips
    print(f"  {name:<26} {tests:>5} tests, {tests - fails - errs - skips:>5} passed, "
          f"{fails} failed, {errs} errored, {skips} skipped")
if not total:
    print("  (none)")

print()
print(f"{total} test(s) that were meant to pass: {total - failed - errored - skipped} passed, "
      f"{failed} failed, {errored} errored, {skipped} skipped")
print(f"{len([g for g in gates if g[1] == 'ok'])} of "
      f"{len([g for g in gates if g[1] not in ('not-requested',)])} gates passed in {elapsed}s")
print("VERDICT: " + ("pass" if rc == 0 else f"fail ({rc} gate(s))"))
PY

cat "$RUN/summary.txt"

verdict="pass"
[ "$rc" -eq 0 ] || verdict="fail"
printf '%s\t%s\t%s\t%s\t%s\t%s\n' \
	"$(date -u +%Y%m%dT%H%M%SZ)" "$COMMIT" \
	"$([ "$DIRTY" = "1" ] && echo dirty || echo clean)" \
	"$verdict" "$elapsed" "$RUN" >> "$HISTORY"

# A fixed path to the most recent run, so a timer's output has somewhere to be
# read from without knowing the stamp.
ln -sfn "$RUN" "$RUNS_DIR/latest"

# Old runs, oldest first, beyond the ones worth keeping. Only directories this
# script makes: a stamp, a dash, and a short hash.
if [ "$KEEP" -gt 0 ]; then
	# shellcheck disable=SC2012
	ls -1d "$RUNS_DIR"/*-* 2>/dev/null \
		| grep -E '/[0-9]{8}T[0-9]{6}Z-[0-9a-f]+$' \
		| sort \
		| head -n -"$KEEP" \
		| while IFS= read -r old; do rm -rf "$old"; done
fi

say "$verdict in ${elapsed}s, $RUN"
exit "$rc"
