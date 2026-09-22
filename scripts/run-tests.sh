#!/usr/bin/env bash
#
# Runs Testaria's tests inside a headless tModLoader server and exits
# non-zero if anything failed.
#
# Everything happens in a scratch save directory, so the run cannot touch a
# real installation's mods, worlds, or players. -tmlsavedirectory points both
# SavePath and SavePathShared at it (Program.TML.cs:298-302).

set -euo pipefail

TML="${TML_PATH:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
MODS_SRC="${MODS_SRC:-$HOME/.local/share/Terraria/tModLoader-dev/Mods}"
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
# BLANK=1 replaces world generation with Testaria's blank substrate.
BLANK_ARG=""
[ "${BLANK:-0}" = "1" ] && BLANK_ARG="-testariablank"
MEM_MAX="${MEM_MAX:-4G}"

# Prefer the system dotnet. The runtime bundled with the Steam install can lag
# the game assemblies: switching to the 1.4.5-dev branch updates tModLoader.dll
# to net10.0 but can leave dotnet/ on 8.0, which then refuses to launch it.
# tModLoader's own tMLMod.targets invokes a plain "dotnet" for the same reason.
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
[ -x "$DOTNET" ] || DOTNET="$TML/dotnet/dotnet"
[ -x "$DOTNET" ] || { echo "no usable dotnet found (set DOTNET)" >&2; exit 2; }

[ -f "$TML/tModLoader.dll" ] || { echo "no tModLoader at $TML (set TML_PATH)" >&2; exit 2; }

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
  [ -f "$MODS_SRC/$mod.tmod" ] || { echo "missing $MODS_SRC/$mod.tmod (build it first)" >&2; exit 2; }
  cp "$MODS_SRC/$mod.tmod" "$SCRATCH/Mods/"
done

# enabled.json is a plain list, so enabling mods needs no Steam and no UI
# (ModOrganizer.cs:748).
printf '[%s]\n' "$(printf '"%s",' $ENABLED | sed 's/,$//')" > "$SCRATCH/Mods/enabled.json"

echo "dotnet:   $DOTNET"
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
echo "scratch:  $SCRATCH"
echo "mods:     $ENABLED"
[ -n "$FILTER" ] && echo "filter:   $FILTER"

# Read-write, not write-only. Opening a FIFO for writing alone blocks until a
# reader appears, and the reader here is the server launched below, so a plain
# "exec 3>" deadlocks before anything starts. Holding both ends also stops the
# server seeing EOF on stdin and quitting.
exec 3<>"$FIFO"

# shellcheck disable=SC2086
nice -n 19 systemd-run --user --quiet --scope -p MemoryMax="$MEM_MAX" \
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
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
tests, fails = int(root.get("tests", 0)), int(root.get("failures", 0))
errors, skipped = int(root.get("errors", 0)), int(root.get("skipped", 0))
print(f"{tests} tests: {tests - fails - errors - skipped} passed, {fails} failed, {errors} errored, {skipped} skipped")
for case in root.iter("testcase"):
    for bad in list(case.findall("failure")) + list(case.findall("error")):
        # The whole message, not just its first line. Assertion messages put
        # the useful part, the expected and actual values, on later lines, so
        # truncating to line one reliably prints the least useful sentence.
        print(f"  {bad.tag.upper()} {case.get('classname')}.{case.get('name')}:")
        for line in (bad.get("message", "") or "(no message)").splitlines():
            print(f"      {line}")
sys.exit(1 if fails or errors else 0)
PY
