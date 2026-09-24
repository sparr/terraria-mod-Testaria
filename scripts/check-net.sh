#!/usr/bin/env bash
#
# The tier 3 gate: a server with a client attached to it.
#
# Driven through the testaria CLI rather than through run-tests.sh, because
# launching a client is the tool's job and because a gate that uses the tool is
# a gate that notices when the tool breaks.
#
# The whole point is that these tests must not quietly pass without a client:
# so this also runs them with no client and asserts they are reported as
# skipped rather than green.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

TOOL="$ROOT/src/Testaria.Tool"
# The whole suite, not a class name. Filtering to "NetTests" stops covering
# tier 3 the moment a tier 3 test is written in a class called something else,
# and it hides a whole class of bug besides: a tier 2 test that counts every
# active player, including the connected client's, can only fail when the whole
# suite runs with a client.
#
# Tier is read back out of the report instead, which needs no naming
# convention to be right.
FILTER="${FILTER:-}"
TIMEOUT="${TIMEOUT:-600}"
WORK="$(mktemp -d -t testaria-net-XXXXXX)"
# Only pass the flag when there is something to filter by; an empty regex
# is not the same thing as no filter.
FILTER_ARGS=()
[ -n "$FILTER" ] && FILTER_ARGS=(--filter "$FILTER")
failures=0

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

echo "=== building ==="
for project in "$ROOT/src/Testaria" "$ROOT/tests/TestariaSelfTest"; do
	nice -n 19 dotnet build "$project" --nologo -v q -clp:ErrorsOnly \
		|| { echo "build failed: $project" >&2; exit 2; }
done

echo
echo "=== with a client ==="
if nice -n 19 dotnet run --project "$TOOL" -- run \
	--mod TestariaSelfTest --client --blank \
	"${FILTER_ARGS[@]}" --name NetTests --timeout "$TIMEOUT" \
	--results "$WORK/net.xml"; then
	echo "--- tier 3 with a client: ok"
else
	echo "--- tier 3 with a client: FAILED" >&2
	failures=$((failures + 1))
fi

echo
echo "=== without a client ==="
# Green here would be the bad kind of green: a netcode test that ran
# single-player and reported a pass. They must be skipped instead.
nice -n 19 dotnet run --project "$TOOL" -- run \
	--mod TestariaSelfTest --blank --speed max \
	"${FILTER_ARGS[@]}" --name NetTestsAlone --timeout "$TIMEOUT" \
	--results "$WORK/alone.xml" --quiet

python3 - "$WORK/alone.xml" <<'PY'
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()

# By tier, out of the report, rather than by matching class names. A gate that
# finds tier 3 tests by naming convention stops covering them silently.
tier3 = [c for c in root.iter("testcase") if c.get("testaria-tier") == "MultiProcess"]
skipped = [c for c in tier3 if c.find("skipped") is not None]

print(f"{len(tier3)} tier 3 test(s) without a client, {len(skipped)} skipped")

if not tier3:
    sys.exit("no tier 3 tests were found at all, so this proves nothing. "
             "Is the report missing testaria-tier?")

if len(skipped) != len(tier3):
    bad = [f"{c.get('classname')}.{c.get('name')}" for c in tier3 if c.find("skipped") is None]
    sys.exit("a tier 3 test did something other than skip with no client attached: "
             + ", ".join(bad))
PY

if [ "$?" -eq 0 ]; then
	echo "--- tier 3 without a client: ok, every test skipped"
else
	echo "--- tier 3 without a client: FAILED" >&2
	failures=$((failures + 1))
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "net check passed"
else
	echo "$failures net check(s) failed" >&2
fi
exit "$failures"
