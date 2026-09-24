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
FILTER="${FILTER:-NetTests}"
TIMEOUT="${TIMEOUT:-600}"
WORK="$(mktemp -d -t testaria-net-XXXXXX)"
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
	--filter "$FILTER" --name NetTests --timeout "$TIMEOUT" \
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
	--filter "$FILTER" --name NetTestsAlone --timeout "$TIMEOUT" \
	--results "$WORK/alone.xml" --quiet

python3 - "$WORK/alone.xml" <<'PY'
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
cases = list(root.iter("testcase"))
skipped = [c for c in cases if c.find("skipped") is not None]

print(f"{len(cases)} tier 3 test(s) without a client, {len(skipped)} skipped")

if not cases:
    sys.exit("no tier 3 tests ran at all, so this proves nothing")

if len(skipped) != len(cases):
    sys.exit("a tier 3 test did something other than skip with no client attached")
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
