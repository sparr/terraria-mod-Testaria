#!/usr/bin/env bash
#
# The templates gate: generate from both templates and use what comes out.
#
# check-packages.sh consumes the packages the way a stranger would. Without
# this, nothing consumes the templates, so nothing notices that a generated
# project has never been compiled, let alone run. An untested onboarding step
# breaks quietly: the suite that stops its own mod from compiling, the mod
# whose internals its tests cannot see, and a post action that makes the
# template itself fail to install while `dotnet new` says nothing and simply
# lists one template instead of two.
#
# Needs a tModLoader install for the mod-tests half, and says so and skips it
# rather than failing if there is none.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

# Named distinctively, because cleanup deletes it from the real Mods folder.
PROBE_MOD="TestariaTemplateProbe"
FEED="$(mktemp -d -t testaria-tfeed-XXXXXX)"
WORK="$(mktemp -d -t testaria-tprobe-XXXXXX)"
failures=0

cleanup() {
	# Uninstall by path, so a developer's own template installs are untouched.
	nice -n 19 dotnet new uninstall "$ROOT/templates/content" >/dev/null 2>&1
	rm -rf "$FEED" "$WORK"
	# The probe package is in the global cache under the version it was packed
	# as, and a later run would consume the stale copy rather than the one it
	# just built. Exactly the bug this gate exists to catch.
	rm -rf "$HOME/.nuget/packages/testaria.core"
	# And the .tmod itself. tMLMod.targets builds a mod by invoking tModLoader,
	# which writes into the save path's Mods folder and takes no say in the
	# matter, so the only way not to leave a probe mod installed is to remove
	# it afterwards.
	rm -f "$MODS_SRC/$PROBE_MOD.tmod"
}
trap cleanup EXIT

check() {
	local name="$1"; shift
	if "$@"; then
		echo "--- $name: ok"
	else
		echo "--- $name: FAILED" >&2
		failures=$((failures + 1))
	fi
}

echo "=== packing Testaria.Core ==="
nice -n 19 dotnet pack "$ROOT/src/Testaria.Core/Testaria.Core.csproj" -c Release --nologo -v q -o "$FEED" \
	|| { echo "pack failed" >&2; exit 2; }

VERSION="$(basename "$(ls "$FEED"/Testaria.Core.*.nupkg | head -1)" .nupkg)"
VERSION="${VERSION#Testaria.Core.}"
echo "packed $VERSION"

# The templates pin a version. If packing produces a different one, every
# generated project fails to restore, and the error names NuGet rather than
# this mismatch.
PINNED="$(grep -ho 'Include="Testaria.Core" Version="[^"]*"' "$ROOT"/templates/content/*/*.csproj \
	| head -1 | sed 's/.*Version="\([^"]*\)".*/\1/')"

if [ "$VERSION" != "$PINNED" ]; then
	echo "--- version pin: FAILED, templates ask for $PINNED but packing produced $VERSION" >&2
	failures=$((failures + 1))
else
	echo "--- version pin: ok, both $VERSION"
fi

echo
echo "=== installing the templates ==="
nice -n 19 dotnet new uninstall "$ROOT/templates/content" >/dev/null 2>&1
nice -n 19 dotnet new install "$ROOT/templates/content" >/dev/null 2>&1 \
	|| { echo "template install failed" >&2; exit 2; }

# Both, by short name, so a template that fails to install is caught here
# rather than as a confusing "no such template" later.
for short in testaria-unit-tests testaria-mod-tests; do
	if nice -n 19 dotnet new list "$short" 2>/dev/null | grep -q "$short"; then
		echo "--- $short installed: ok"
	else
		echo "--- $short installed: FAILED" >&2
		failures=$((failures + 1))
	fi
done

# A feed holding only what was just packed, so nothing is answered from
# nuget.org or from a stale cache entry.
cat > "$WORK/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="probe" value="$FEED" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

echo
echo "=== tier 0: generated, built, and run ==="
(
	cd "$WORK" || exit 1
	nice -n 19 dotnet new testaria-unit-tests -n ProbeUnitTests >/dev/null || exit 1
	# The point of this template is that it runs wherever dotnet does, so the
	# check is a real test run rather than a build.
	nice -n 19 dotnet test ProbeUnitTests --nologo -v q
)
check "the tier 0 template's tests pass" test $? -eq 0

echo
echo "=== tiers 1 and 2: generated and built into a .tmod ==="
if [ ! -f "$TML_PATH/tMLMod.targets" ]; then
	echo "--- skipped, no tModLoader at $TML_PATH (set TML_PATH)"
else
	(
		cd "$WORK" || exit 1
		nice -n 19 dotnet new testaria-mod-tests -n "$PROBE_MOD" --subject ExampleMod >/dev/null || exit 1

		# tMLMod.targets builds a mod by invoking tModLoader, which writes the
		# .tmod into the save path's Mods folder and offers no way to redirect
		# it. So the probe is named distinctively and removed on the way out.
		nice -n 19 dotnet build "$PROBE_MOD" --nologo -v q -clp:ErrorsOnly \
			-p:tModLoaderSteamPath="$TML_PATH" >/dev/null || exit 1
	)
	built=$?

	check "the tier 1 and 2 template builds" test $built -eq 0

	if [ "$built" -eq 0 ]; then
		if [ -f "$MODS_SRC/$PROBE_MOD.tmod" ]; then
			echo "--- it produced a .tmod: ok"
		else
			echo "--- it produced a .tmod: FAILED" >&2
			failures=$((failures + 1))
		fi
	fi
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "templates check passed"
else
	echo "$failures template check(s) failed" >&2
fi

exit $((failures > 0 ? 1 : 0))
