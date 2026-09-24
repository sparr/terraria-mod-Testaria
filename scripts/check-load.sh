#!/usr/bin/env bash
#
# The load gate: the arena under pressure, from PLAN.md section 8.4.
#
# Deliberate rather than automatic. It leases three hundred boxes, asks for
# every size class including the spanning columns nothing else uses, and fills
# a box with more entities than Terraria's pool can hold. That is a minute of
# work aimed at one component, which is worth running when the arena changes
# and not on every commit.
#
# Run it with:
#   scripts/check-load.sh
#   RUN_LOAD=1 scripts/run-all.sh      (as part of the full sweep)

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

TOOL="$ROOT/src/Testaria.Tool"
TIMEOUT="${TIMEOUT:-900}"
WORK="$(mktemp -d -t testaria-load-XXXXXX)"

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

echo "=== building ==="
for project in "$ROOT/src/Testaria" "$ROOT/tests/TestariaLoadTest"; do
	nice -n 19 dotnet build "$project" --nologo -v q -clp:ErrorsOnly \
		|| { echo "build failed: $project" >&2; exit 2; }
done

echo
echo "=== the arena under load ==="
# --measure does double duty: it collects the box costs, and it lengthens the
# quarantine, which keeps far more boxes alive at once and so puts real
# pressure on carving and the free list.
nice -n 19 dotnet run --project "$TOOL" -- run \
	--mod TestariaLoadTest --blank --speed max --measure \
	--name ArenaLoad --require 300 --timeout "$TIMEOUT" \
	--results "$WORK/load.xml" || { echo "--- load: FAILED" >&2; exit 1; }

python3 - "$WORK/load-arena.tsv" <<'PY'
import sys, collections

rows = []
with open(sys.argv[1], encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        parts = line.rstrip("\n").split("\t")
        rows.append(dict(zip(header, parts + [""] * (len(header) - len(parts)))))

sizes = collections.Counter((int(r["grantedW"]), int(r["grantedH"])) for r in rows)
columns = [r for r in rows if int(r["grantedH"]) > 256]
slow = max((float(r["restoreMs"]) for r in rows), default=0.0)
noisy = [r for r in rows if int(r["ticksToQuiet"]) > 60]

print(f"{len(rows)} boxes leased and returned")
print("sizes: " + ", ".join(f"{w}x{h} x{n}" for (w, h), n in sorted(sizes.items())))
print(f"spanning columns: {len(columns)}")
print(f"slowest restore: {slow:.1f} ms")

if not columns:
    sys.exit("no spanning column was leased, so the column path was not exercised")

if noisy:
    sys.exit(f"{len(noisy)} box(es) took more than 60 ticks to go quiet, which the quarantine does not cover")
PY

rc=$?

echo
if [ "$rc" -eq 0 ]; then
	echo "load check passed"
else
	echo "load check FAILED" >&2
fi
exit "$rc"
