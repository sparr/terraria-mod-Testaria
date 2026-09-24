#!/usr/bin/env bash
#
# The packaging gate: builds the NuGet packages and consumes them the way a
# stranger would.
#
# Unit tests cannot see any of this. They test the analyzer by instantiating
# it, the tool by calling into it, and the MSBuild by not testing it at all,
# so every one of them passes while a package ships without its analyzer,
# without its targets, or with an XML comment MSBuild refuses to load. All
# three of those are caught here and nowhere else.
#
# Needs a tModLoader install for the last two probes; says so and skips them
# rather than failing if there is none.

set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$HERE")"
# shellcheck source=scripts/paths.sh
. "$HERE/paths.sh"

FEED="$(mktemp -d -t testaria-feed-XXXXXX)"
WORK="$(mktemp -d -t testaria-probe-XXXXXX)"
failures=0

cleanup() {
	rm -rf "$FEED" "$WORK"
	# The probe packages are in the global cache under the version they were
	# packed as, and a later run would silently consume the stale copy rather
	# than the one it just built. Exactly the bug this gate exists to catch,
	# so it must not be the bug this gate creates.
	rm -rf "$HOME/.nuget/packages/testaria.core" \
	       "$HOME/.nuget/packages/testaria.unit" \
	       "$HOME/.nuget/packages/testaria.sdk" \
	       "$HOME/.nuget/packages/testaria.tool"
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

echo "=== packing ==="
for project in Testaria.Core Testaria.Unit Testaria.Sdk Testaria.Tool; do
	nice -n 19 dotnet pack "$ROOT/src/$project/$project.csproj" -c Release --nologo -v q -o "$FEED" \
		|| { echo "pack failed: $project" >&2; exit 2; }
done
VERSION="$(basename "$(ls "$FEED"/Testaria.Core.*.nupkg | head -1)" .nupkg)"
VERSION="${VERSION#Testaria.Core.}"
echo "packed $VERSION into $FEED"

# A probe project, its feed pinned to the packages just built so that nothing
# reaches nuget.org and nothing is answered from a stale cache entry.
probe() {
	local dir="$WORK/$1"; shift
	mkdir -p "$dir"
	cat > "$dir/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="probe" value="$FEED" />
  </packageSources>
</configuration>
XML
	echo "$dir"
}

# 1. The analyzer ships inside Testaria.Core and applies to a consumer.
analyzer_probe() {
	local dir; dir="$(probe analyzer)"
	cat > "$dir/Probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Testaria.Core" Version="$VERSION" /></ItemGroup>
</Project>
XML
	cat > "$dir/Subject.cs" <<'CS'
namespace Terraria.ID
{
	public static class ContentSamples
	{
		public static System.Collections.Generic.Dictionary<int, int> ItemsByType = new();
	}
}

public class Subject
{
	public int Loaded() => Terraria.ID.ContentSamples.ItemsByType.Count;
}
CS
	local out="$dir/build.log"
	nice -n 19 dotnet build "$dir" --nologo -v q > "$out" 2>&1
	grep -q "TSTA001" "$out" || { echo "expected TSTA001 from the packaged analyzer:" >&2; tail -5 "$out" >&2; return 1; }
}

# 2. Testaria.Unit refuses to build without a tModLoader install, and says so.
unit_without_tml_probe() {
	local dir; dir="$(probe unit-no-tml)"
	cat > "$dir/Probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <tModLoaderSteamPath>$WORK/definitely-not-an-install</tModLoaderSteamPath>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Testaria.Unit" Version="$VERSION" /></ItemGroup>
</Project>
XML
	local out="$dir/build.log"
	nice -n 19 dotnet build "$dir" --nologo -v q > "$out" 2>&1
	grep -q "TSTU001" "$out" || { echo "expected TSTU001 with no install:" >&2; tail -5 "$out" >&2; return 1; }
}

# 3. With an install, the same package puts the game's types in scope.
unit_with_tml_probe() {
	local dir; dir="$(probe unit-with-tml)"
	cat > "$dir/Probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Testaria.Unit" Version="$VERSION" /></ItemGroup>
</Project>
XML
	cat > "$dir/Subject.cs" <<'CS'
using Terraria;
using Terraria.ID;

public class Subject
{
	// Compiles only if the tModLoader references came through the package.
	public int Damage() => new Item().damage;

	public int Vanilla() => ItemID.Count;

	// And the assertions came with them.
	public void Check() => Testaria.Assert.True(true);
}
CS
	local out="$dir/build.log"
	TML_PATH="$TML_PATH" nice -n 19 dotnet build "$dir" --nologo -v q > "$out" 2>&1 \
		|| { echo "expected a clean build against the install:" >&2; tail -10 "$out" >&2; return 1; }
}

# 4. Testaria.Sdk runs a real suite from MSBuild and carries the tool with it.
sdk_probe() {
	local dir; dir="$(probe sdk)"
	cat > "$dir/Probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <TestariaRunName>PackageProbe</TestariaRunName>
    <TestariaFilter>SpineTests</TestariaFilter>
    <TestariaTimeout>300</TestariaTimeout>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Testaria.Sdk" Version="$VERSION" /></ItemGroup>
  <ItemGroup><TestariaTestMod Include="TestariaSelfTest" /></ItemGroup>
</Project>
XML
	local out="$dir/build.log"
	TML_PATH="$TML_PATH" nice -n 19 dotnet build "$dir" -t:TestariaRun --nologo -v m > "$out" 2>&1 \
		|| { echo "expected the packaged SDK to run the suite green:" >&2; tail -15 "$out" >&2; return 1; }
	grep -q "0 failed, 0 errored" "$out" || { echo "the run did not report green:" >&2; tail -5 "$out" >&2; return 1; }
	[ -f "$dir/TestResults/PackageProbe.xml" ] || { echo "no report where the SDK said it would be" >&2; return 1; }
}

echo
echo "=== consuming ==="
check "analyzer ships in Testaria.Core" analyzer_probe
check "Testaria.Unit reports a missing install" unit_without_tml_probe

if [ -f "$TML_PATH/tModLoader.dll" ]; then
	check "Testaria.Unit puts the game in scope" unit_with_tml_probe
	check "Testaria.Sdk runs a suite from MSBuild" sdk_probe
else
	echo "--- skipped the install-dependent probes: no tModLoader at $TML_PATH"
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "package check passed"
else
	echo "$failures package check(s) failed" >&2
fi
exit "$failures"
