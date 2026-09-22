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
WORK="${WORK:-$(mktemp -d -t examplemod-build-XXXXXX)}"

[ -f "$TML/tMLMod.targets" ] || { echo "no tModLoader at $TML (set TML_PATH)" >&2; exit 2; }
[ -d "$SRC" ] || { echo "no ExampleMod sources at $SRC (set EXAMPLEMOD_SRC)" >&2; exit 2; }

echo "copying ExampleMod sources to $WORK"
rm -rf "$WORK/ExampleMod"
cp -r "$SRC" "$WORK/ExampleMod"
rm -rf "$WORK/ExampleMod/bin" "$WORK/ExampleMod/obj" "$WORK/ExampleMod/Old"

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

dotnet build "$WORK/ExampleMod/ExampleMod.csproj" --nologo -v q
echo "ExampleMod.tmod built"
