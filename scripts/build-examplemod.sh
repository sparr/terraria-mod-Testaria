#!/usr/bin/env bash
#
# Builds ExampleMod into a .tmod so it can be used as a calibration subject.
#
# ExampleMod ships as source inside the tModLoader repository, not with the
# game, and its own .csproj imports the repository's targets, which expect the
# decompiled src/ tree that only setup-cli.sh produces. Rather than run setup,
# this copies the sources somewhere scratch and gives them a .csproj that
# imports the *installed* tMLMod.targets, exactly as any ordinary mod does.
#
# The checkout is never modified.

set -euo pipefail

TML="${TML_PATH:-$HOME/.local/share/Steam/steamapps/common/tModLoader}"
SRC="${EXAMPLEMOD_SRC:-}"
# A stable location rather than a fresh mktemp each run, for two reasons: the
# build is then incremental instead of recompiling 451 files every time, and
# the resulting ExampleMod.dll has a path that a test project can reference at
# compile time. Override WORK to build somewhere else.
WORK="${WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/testaria/examplemod-build}"
mkdir -p "$WORK"

[ -f "$TML/tMLMod.targets" ] || { echo "no tModLoader at $TML (set TML_PATH)" >&2; exit 2; }
[ -d "$SRC" ] || { echo "no ExampleMod sources at $SRC (set EXAMPLEMOD_SRC)" >&2; exit 2; }

echo "copying ExampleMod sources to $WORK"
mkdir -p "$WORK/ExampleMod"
# Mirror the sources but keep bin/ and obj/, so a rebuild after a one-line fix
# is quick. --delete makes a source file deleted upstream disappear here too,
# which a plain cp would leave behind to be compiled forever.
rsync -a --delete \
  --exclude "bin/" --exclude "obj/" --exclude "Old/" --exclude ".vs/" \
  --exclude "ExampleMod.csproj" \
  "$SRC/" "$WORK/ExampleMod/"

cat > "$WORK/ExampleMod/ExampleMod.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>ExampleMod</AssemblyName>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <!-- Someone else's code; their warnings are not ours to fail on. -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <Import Project="$TML/tMLMod.targets" />
  <ItemGroup>
    <Compile Remove="Old/**" />
    <None Remove="Old/**" />
  </ItemGroup>
</Project>
EOF

nice -n 19 dotnet build "$WORK/ExampleMod/ExampleMod.csproj" --nologo -v q
echo "ExampleMod.tmod built"
echo "assembly at $WORK/ExampleMod/bin/Debug/net10.0/ExampleMod.dll"
