#!/usr/bin/env bash
#
# Runs the tests that asked for a fresh world, one process each.
#
# A runner sharing its world with other tests cannot honestly claim to have
# provided a fresh one, so an ordinary run reports those tests as skipped. The
# only honest way to give a test a world nobody else has touched is to give it
# a process nobody else is using, which is what this does.
#
# Slow by construction: each test pays a full server start. That is the price
# of the isolation it asked for, and why [FreshWorld] is opt-in.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d -t testaria-fresh-XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

echo "cataloguing tests..."
MODE=list BLANK=1 RESULTS_OUT="$WORK/tests.tsv" "$HERE/run-tests.sh" >/dev/null 2>&1 || {
	echo "could not list tests" >&2
	exit 2
}

# Column 2 is freshWorld, 3 is the class, 4 is the name.
mapfile -t fresh < <(awk -F'\t' 'NR > 1 && $2 == "yes" { print $3 "." $4 }' "$WORK/tests.tsv")

if [ "${#fresh[@]}" -eq 0 ]; then
	echo "no tests asked for a fresh world"
	exit 0
fi

echo "${#fresh[@]} test(s) asked for a fresh world; each gets its own server"
failures=0

for name in "${fresh[@]}"; do
	echo
	echo "=== $name ==="

	if FILTER="$name" FRESH_WORLD=1 BLANK=1 RUN_NAME="Fresh" \
	   RESULTS_OUT="$WORK/$(echo "$name" | tr -c 'A-Za-z0-9._-' '_').xml" \
	   "$HERE/run-tests.sh" 2>&1 | tail -3; then
		echo "--- ok"
	else
		echo "--- FAILED" >&2
		failures=$((failures + 1))
	fi
done

echo
if [ "$failures" -eq 0 ]; then
	echo "all ${#fresh[@]} fresh-world test(s) passed"
else
	echo "$failures of ${#fresh[@]} fresh-world test(s) failed" >&2
fi
exit "$failures"
