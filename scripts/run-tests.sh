#!/usr/bin/env bash
#
# Runs Testaria's tests inside a headless tModLoader server and exits
# non-zero if anything failed.
#
# Everything happens in a scratch save directory, so the run cannot touch a
# real installation's mods, worlds, or players. -tmlsavedirectory points both
# SavePath and SavePathShared at it (Program.TML.cs:298-302).

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# TML_PATH and MODS_SRC, with the only defaults anything here assumes.
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

TML="$TML_PATH"
RUN_NAME="${RUN_NAME:-TestariaSelfTest}"
ENABLED="${ENABLED:-Testaria TestariaSelfTest}"
TIMEOUT="${TIMEOUT:-600}"
SEED="${SEED:-42}"
# FILTER narrows the run to matching tests; see TestFilter for the syntax.
FILTER="${FILTER:-}"
# MODE=run executes the tests; MODE=list only catalogues them, which is how a
# harness finds out which tests want a world of their own.
MODE="${MODE:-run}"
# FRESH_WORLD=1 promises this process has a world to itself, which is what lets
# tests marked [FreshWorld] be honoured rather than skipped.
FRESH_ARG=""
[ "${FRESH_WORLD:-0}" = "1" ] && FRESH_ARG="-testariafreshworld"
# ISOLATE_MUTATORS=1 asks that every test marked [MutatesGlobalState] be given a
# world of its own, rather than trusting it to put back what it changed. Off by
# default. It needs FRESH_WORLD=1 as well, since a world per test is something
# the harness provides; asking without it is reported as a skip per test rather
# than silently ignored.
ISOLATE_ARG=""
[ "${ISOLATE_MUTATORS:-0}" = "1" ] && ISOLATE_ARG="-testariaisolatemutators"
# BEHAVIOUR=1 asks for the sweep's behaviour tests, which spawn every piece of
# content and run its own update rather than reading it. Off by default: the
# cost is a box per piece of content across every enabled mod.
BEHAVIOUR_ARG=""
[ "${BEHAVIOUR:-0}" = "1" ] && BEHAVIOUR_ARG="-testariabehaviour"
# ECONOMY=1 asks for the checks whose failure is a balance claim rather than a
# defect: today that is the recipe that crafts its own ingredient. Off by
# default because a cheat mod means it, not because it costs anything.
ECONOMY_ARG=""
[ "${ECONOMY:-0}" = "1" ] && ECONOMY_ARG="-testariaeconomy"
# BLANK=1 replaces world generation with Testaria's blank substrate.
BLANK_ARG=""
[ "${BLANK:-0}" = "1" ] && BLANK_ARG="-testariablank"
# SPEED fast-forwards the whole run: "max" for as fast as the CPU allows, or a
# number of ticks per second for a rate that does not vary by machine. Tests
# marked [RealTime] still run at 60. Simulated behaviour is unchanged either
# way; only the wall clock differs.
SPEED_ARG=""
[ -n "${SPEED:-}" ] && SPEED_ARG="-testariaspeed $SPEED"
# RUN_SEED shifts every test's own randomness at once, which is how a suite is
# rerun against different rolls to find out whether it depends on luck. Not to
# be confused with SEED above, which is the world's: one decides the terrain,
# the other decides what the dice do once a test starts.
RUN_SEED_ARG=""
[ -n "${RUN_SEED:-}" ] && RUN_SEED_ARG="-testariaseed $RUN_SEED"
# The mods this gate installed, which the run is told to insist on. A mod that
# throws during its load pass is disabled by the game and everything carries
# on, so without this the suite aimed at it finds nothing and the report comes
# back clean. Measured: a mod that threw from Load() took its test mod with it
# and the run reported "0 tests: 0 passed" and exited 0.
REQUIRE_MODS_ARG="-testariarequiremods $(printf '%s,' $ENABLED | sed 's/,$//')"
MEM_MAX="${MEM_MAX:-4G}"
# The cap is applied with a transient systemd scope, which needs a systemd user
# manager and not merely the systemd-run binary. A CI runner commonly has the
# second and not the first, and the failure is not a warning: the server never
# starts, so every gate that comes through here fails at once and says nothing
# about the framework.
#
# Probed rather than assumed, because there is no reliable way to ask. Running
# the real thing against `true` costs one process and answers exactly the
# question that matters. MEM_CAP=0 skips the probe and runs uncapped, which is
# how the uncapped path gets exercised on a machine that could cap.
MEM_CAP="${MEM_CAP:-1}"
CAP=()
CAP_WHY="no systemd-run"
if [ "$MEM_CAP" != "1" ]; then
  CAP_WHY="MEM_CAP=$MEM_CAP"
elif command -v systemd-run >/dev/null; then
  if systemd-run --user --quiet --scope -p MemoryMax="$MEM_MAX" true >/dev/null 2>&1; then
    CAP=(systemd-run --user --quiet --scope -p MemoryMax="$MEM_MAX")
  else
    CAP_WHY="no usable systemd user manager"
  fi
fi

# Prefer the system dotnet. The runtime bundled with the Steam install can lag
# the game assemblies: switching to the 1.4.5-dev branch updates tModLoader.dll
# to net10.0 but can leave dotnet/ on 8.0, which then refuses to launch it.
# tModLoader's own tMLMod.targets invokes a plain "dotnet" for the same reason.
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
[ -x "$DOTNET" ] || DOTNET="$TML/dotnet/dotnet"
[ -x "$DOTNET" ] || { echo "no usable dotnet found (set DOTNET)" >&2; exit 2; }

[ -f "$TML/tModLoader.dll" ] || { echo "no tModLoader at $TML (set TML_PATH)" >&2; exit 2; }
# An install is more than its main assembly, and a build from source is the
# case where that stops being obvious: setup-cli produces a directory laid out
# like the Steam one, but pointed at the wrong level of it, or at a build that
# did not finish, the server starts and then dies on a native library. Checked
# here, where the path is still the thing being talked about.
[ -d "$TML/Libraries/Native/Linux" ] || [ "$(uname -s)" != "Linux" ] \
  || { echo "no Libraries/Native/Linux under $TML, so this is not a complete install" >&2; exit 2; }

ROOT="$(dirname "$HERE")"

# Build every enabled mod that has a project here, before copying any .tmod.
# Without this a gate happily runs whatever .tmod was last built, so a test
# added an hour ago is simply absent from the report and the run still passes,
# which looks identical to the test passing. The mod projects are outside
# Testaria.slnx because they need a tModLoader install, so a plain
# `dotnet build` at the root does not cover them.
# MOD_PROJECT_PATH adds directories to search, colon separated, for suites that
# live outside this repository. A test suite belongs with the mod it tests, so
# that is the normal case rather than the exotic one: ExampleMod's suite lives
# in the tModLoader checkout beside ExampleMod itself.
if [ "${BUILD:-1}" = "1" ]; then
  search="$ROOT/src:$ROOT/tests${MOD_PROJECT_PATH:+:$MOD_PROJECT_PATH}"

  for mod in $ENABLED; do
    # Some mods are built by something else and must not be built from a
    # project found on the search path. ExampleMod is the case in point: its
    # own .csproj is in the tModLoader checkout and cannot build without the
    # decompiled src/ tree, so build-examplemod.sh handles it separately.
    case " ${BUILD_SKIP:-} " in *" $mod "*) continue ;; esac

    found=""
    while IFS= read -r base; do
      [ -n "$base" ] || continue
      [ -f "$base/$mod/$mod.csproj" ] || continue
      found="$base/$mod"
      break
    done <<< "$(echo "$search" | tr ':' '\n')"

    [ -n "$found" ] || continue

    echo "building:  $mod"
    nice -n 19 "$DOTNET" build "$found" --nologo -v q -clp:ErrorsOnly \
      || { echo "build failed: $mod ($found)" >&2; exit 2; }
  done
fi

SCRATCH="$(mktemp -d -t testaria-run-XXXXXX)"
LOG="$SCRATCH/server.log"
FIFO="$SCRATCH/stdin"
SERVER_PID=""
XVFB_PID=""

# A dedicated framebuffer on an unused display number. -server is headless and
# should never touch a display, but FNA initialises SDL early enough that a
# misconfiguration would otherwise land on the real desktop. Audio goes to the
# dummy driver for the same reason.
pick_display() {
  local n=90
  while [ -e "/tmp/.X11-unix/X$n" ] && [ "$n" -lt 160 ]; do n=$((n + 1)); done
  echo ":$n"
}

cleanup() {
  # Kill by tracked PID, never by name: a pattern like the script's own name
  # matches the shell running it.
  [ -n "$SERVER_PID" ] && kill "$SERVER_PID" 2>/dev/null || true
  [ -n "$XVFB_PID" ] && kill "$XVFB_PID" 2>/dev/null || true
  exec 3>&- 2>/dev/null || true
  if [ "${KEEP_SCRATCH:-0}" = "1" ]; then
    echo "scratch kept at $SCRATCH"
  else
    rm -rf "$SCRATCH"
  fi
}
trap cleanup EXIT

mkdir -p "$SCRATCH/Mods" "$SCRATCH/Worlds"
mkfifo "$FIFO"

for mod in $ENABLED; do
  if [ ! -f "$MODS_SRC/$mod.tmod" ]; then
    echo "missing $MODS_SRC/$mod.tmod (build it first)" >&2
    # A build writes its .tmod to the save path of the tModLoader that built
    # it, and which folder that is depends on the build's purpose: Mods under
    # tModLoader, tModLoader-preview, or tModLoader-dev. So the usual cause of
    # this is a correct build and the wrong MODS_SRC, which matters most where
    # nobody chose the install by hand. Name the alternatives rather than
    # leaving it to be guessed.
    for base in "$HOME/.local/share/Terraria" "$HOME/Library/Application Support/Terraria"; do
      [ -d "$base" ] || continue
      for candidate in "$base"/tModLoader*/Mods; do
        [ -d "$candidate" ] && [ "$candidate" != "$MODS_SRC" ] \
          && echo "  a Mods directory that does exist: $candidate (set MODS_SRC)" >&2
      done
    done
    exit 2
  fi
  cp "$MODS_SRC/$mod.tmod" "$SCRATCH/Mods/"
done

# enabled.json is a plain list, so enabling mods needs no Steam and no UI
# (ModOrganizer.cs:748).
printf '[%s]\n' "$(printf '"%s",' $ENABLED | sed 's/,$//')" > "$SCRATCH/Mods/enabled.json"

echo "dotnet:   $DOTNET"
if [ "${#CAP[@]}" -gt 0 ]; then
  echo "memory:   capped at $MEM_MAX"
else
  echo "memory:   uncapped ($CAP_WHY)"
fi
DISPLAY_NUM="$(pick_display)"
if command -v Xvfb >/dev/null; then
  Xvfb "$DISPLAY_NUM" -screen 0 640x480x24 -nolisten tcp >/dev/null 2>&1 &
  XVFB_PID=$!
  export DISPLAY="$DISPLAY_NUM"
  echo "display:  $DISPLAY_NUM (Xvfb pid $XVFB_PID)"
else
  export SDL_VIDEODRIVER=dummy
  echo "display:  none, SDL_VIDEODRIVER=dummy"
fi

export SDL_AUDIODRIVER=dummy

echo "seed:     $SEED${BLANK_ARG:+  (blank world)}"
[ -n "$SPEED_ARG" ] && echo "speed:    ${SPEED}"
[ -n "$RUN_SEED_ARG" ] && echo "run seed: ${RUN_SEED}"
echo "scratch:  $SCRATCH"
echo "mods:     $ENABLED"
[ -n "$FILTER" ] && echo "filter:   $FILTER"

# Read-write, not write-only. Opening a FIFO for writing alone blocks until a
# reader appears, and the reader here is the server launched below, so a plain
# "exec 3>" deadlocks before anything starts. Holding both ends also stops the
# server seeing EOF on stdin and quitting.
exec 3<>"$FIFO"

# shellcheck disable=SC2086
nice -n 19 "${CAP[@]}" \
  env --chdir="$TML" \
      LD_LIBRARY_PATH="$TML/Libraries/Native/Linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
      SDL_AUDIODRIVER=dummy \
  "$DOTNET" ./tModLoader.dll -server \
    -tmlsavedirectory "$SCRATCH" \
    -nosteam \
    -autocreate 1 \
    -world "$SCRATCH/Worlds/testaria.wld" \
    -worldname testaria \
    -seed "$SEED" \
    $BLANK_ARG \
    $FRESH_ARG \
    $ISOLATE_ARG \
    $BEHAVIOUR_ARG \
    $ECONOMY_ARG \
    $SPEED_ARG \
    $RUN_SEED_ARG \
    $REQUIRE_MODS_ARG \
    -players 1 \
    -port 7777 \
    -password "" \
  < "$FIFO" > "$LOG" 2>&1 &
SERVER_PID=$!

echo "server:   pid $SERVER_PID, log $LOG"

# Wait for the world to finish generating and the server to accept commands.
deadline=$(( SECONDS + TIMEOUT ))
until grep -qE "Server started|Listening on port" "$LOG" 2>/dev/null; do
  kill -0 "$SERVER_PID" 2>/dev/null || { echo "server exited early:" >&2; tail -40 "$LOG" >&2; exit 2; }
  [ "$SECONDS" -lt "$deadline" ] || { echo "timed out waiting for server start" >&2; tail -40 "$LOG" >&2; exit 2; }
  sleep 2
done

echo "world ready after ${SECONDS}s"
echo "server up, sending: testaria $MODE"
if [ "$MODE" = "list" ]; then
  echo "testaria list $FILTER" >&3
  RESULTS="$SCRATCH/Testaria/tests.tsv"
else
  echo "testaria run $RUN_NAME $FILTER" >&3
  RESULTS="$SCRATCH/Testaria/$RUN_NAME.xml"
fi
until [ -f "$RESULTS" ]; do
  kill -0 "$SERVER_PID" 2>/dev/null || { echo "server died during the run:" >&2; tail -40 "$LOG" >&2; exit 2; }
  [ "$SECONDS" -lt "$deadline" ] || { echo "timed out waiting for results" >&2; tail -60 "$LOG" >&2; exit 2; }
  sleep 2
done

echo "exit" >&3
wait "$SERVER_PID" 2>/dev/null || true
SERVER_PID=""

if [ -n "${RESULTS_OUT:-}" ]; then
  mkdir -p "$(dirname "$RESULTS_OUT")"
  cp "$RESULTS" "$RESULTS_OUT"
  echo "results copied to $RESULTS_OUT"
fi

if [ "$MODE" = "list" ]; then
  # Nothing ran, so there is no pass or fail to report.
  cat "$RESULTS"
  exit 0
fi

python3 - "$RESULTS" <<'PY'
import os, sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
tests, fails = int(root.get("tests", 0)), int(root.get("failures", 0))
problems, skipped = int(root.get("errors", 0)), int(root.get("skipped", 0))
# Blocked tests are written as errors so that CI reaches the same verdict the
# runner does, but they mean something different and are worth counting apart:
# a blocked test never ran, and says nothing at all about its subject.
blocked = sum(1 for case in root.iter("testcase")
              for bad in case.findall("error")
              if bad.get("type") == "Testaria.Blocked")
errors = problems - blocked
summary = f"{tests} tests: {tests - fails - problems - skipped} passed, {fails} failed, {errors} errored"
if blocked:
    summary += f", {blocked} blocked"
print(summary + f", {skipped} skipped")
for case in root.iter("testcase"):
    for bad in list(case.findall("failure")) + list(case.findall("error")):
        # The whole message, not just its first line. Assertion messages put
        # the useful part, the expected and actual values, on later lines, so
        # truncating to line one reliably prints the least useful sentence.
        print(f"  {bad.tag.upper()} {case.get('classname')}.{case.get('name')}:")
        for line in (bad.get("message", "") or "(no message)").splitlines():
            print(f"      {line}")
# A suite that skipped everything is not a suite that passed. This happened
# for real: tModLoader updated, ExampleMod's build stopped loading against it,
# and all 924 calibration tests skipped themselves politely while the gate
# reported success. MIN_TESTS is how a gate says it expected to prove something.
minimum = int(os.environ.get("MIN_TESTS", "0") or 0)
ran = tests - skipped

if minimum and ran < minimum:
    print(f"only {ran} test(s) ran, but MIN_TESTS={minimum} was expected; "
          "check that the mod under test actually loaded")
    sys.exit(1)

# problems, not errors: a run that could not run part of itself has not
# established what it was asked to, even if everything that did run passed.
sys.exit(1 if fails or problems else 0)
PY
