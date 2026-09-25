#!/usr/bin/env bash
#
# Proves that a failing test actually fails.
#
# A framework that can only report green is indistinguishable from one that
# works, so this runs a mod of deliberately broken tests and checks that each
# one arrives in the report as the right *kind* of problem. Exiting non-zero is
# not enough on its own: a failed assertion and a broken test must stay
# distinguishable all the way out, or the report cannot be trusted to tell an
# author whether their mod or their test is at fault.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mktemp -d -t testaria-red-XXXXXX)"

# RESULTS_DIR, when a CI run sets it, is where the report is kept rather than
# discarded. Copied on the way out and not written there directly, because the
# checks below are about a report this script alone is responsible for.
cleanup() {
	if [ -n "${RESULTS_DIR:-}" ] && [ -f "$OUT/red.xml" ]; then
		cp "$OUT/red.xml" "$RESULTS_DIR/red-path.xml"
	fi
	rm -rf "$OUT"
}
trap cleanup EXIT

echo "running the deliberately broken suite..."

RESULTS_OUT="$OUT/red.xml" \
ENABLED="Testaria TestariaRedTest" \
RUN_NAME="RedCheck" \
BLANK=1 \
  "$HERE/run-tests.sh"
rc=$?

fail() { echo "RED CHECK FAILED: $*" >&2; exit 1; }

[ "$rc" -eq 1 ] || fail "expected the harness to exit 1 on a red suite, got $rc"
[ -f "$OUT/red.xml" ] || fail "no report was written"

python3 - "$OUT/red.xml" <<'PY'
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
problems = []

def expect(label, got, want):
    if got != want:
        problems.append(f"{label}: expected {want}, got {got}")

# Two assertion failures (one direct, one timeout) and four errors: one
# thrown, one malformed and caught at discovery, one whose assertions all
# passed but whose box was contaminated, and one asking for a box the world
# cannot hold, which has to be reported rather than hanging the run.
expect("tests",    int(root.get("tests", 0)),    6)
expect("failures", int(root.get("failures", 0)), 2)
expect("errors",   int(root.get("errors", 0)),   4)
expect("skipped",  int(root.get("skipped", 0)),  0)

outcomes = {}
for case in root.iter("testcase"):
    kind = "passed"
    if case.find("failure") is not None:
        kind = "failure"
    elif case.find("error") is not None:
        kind = "error"
    elif case.find("skipped") is not None:
        kind = "skipped"
    outcomes[case.get("name")] = kind

want = {
    "Deliberately_fails_an_assertion": "failure",
    "Deliberately_times_out":          "failure",
    "Deliberately_throws":             "error",
    "Deliberately_malformed":          "error",
    "Deliberately_contaminates_its_own_box": "error",
    "Deliberately_asks_for_an_impossible_box": "error",
}

for name, kind in want.items():
    if name not in outcomes:
        problems.append(f"{name}: missing from the report entirely")
    elif outcomes[name] != kind:
        problems.append(f"{name}: expected {kind}, got {outcomes[name]}")

# The message is the whole point of a failure report, so an empty one is a bug.
for case in root.iter("testcase"):
    for bad in list(case.findall("failure")) + list(case.findall("error")):
        if not (bad.get("message") or "").strip():
            problems.append(f"{case.get('name')}: reported with an empty message")

if problems:
    print("RED CHECK FAILED:")
    for p in problems:
        print(f"  {p}")
    sys.exit(1)

print(f"red check passed: {len(outcomes)} deliberate problems, each reported as the right kind")
for name in sorted(outcomes):
    print(f"  {outcomes[name]:<8} {name}")
PY
